﻿using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

namespace CClash
{
    public class CClashInetServer : CClashServerBase, IDisposable
    {
        TcpListener listener;
        Mutex inetlock;

        AutoResetEvent incoming = new AutoResetEvent(false);
        Queue<Stream> incomingClients = new Queue<Stream>();
        List<Thread> threads = new List<Thread>();

        public CClashInetServer()
            : base()
        {
            var tcount = Settings.HashThreadCount;
            do {
                var t = new Thread(ServeRequestThreadFunc);
                t.IsBackground = true;
                threads.Add(t);
                t.Start();
            } while (threads.Count < tcount);

            
        }

        protected override void YieldLocks() {
            // do nothing, we need the locks as we are multi-threaded.
        }

        public override bool Connected(Stream s)
        {
            return s.CanWrite;
        }

        public override void Stop()
        {
            quitnow = true;
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                } catch { }

                try {
                    inetlock.ReleaseMutex();
                } catch { }

            }
        }


        public override Stream AwaitConnection(object service)
        {
            var iar = listener.BeginAcceptSocket(null, null);
            var idletimer = new Stopwatch();
            idletimer.Start();
            Stream rv = null;
            do
            {
                if (iar.AsyncWaitHandle.WaitOne(500)) {
                    var sock = listener.EndAcceptSocket(iar);
                    rv = new NetworkStream(sock);
                    break;
                } else {
                    if (idletimer.ElapsedMilliseconds > base.ExitAfterIdleSec * 1000) {
                        Logging.Emit("exiting.. server has been idle for {0} sec", idletimer.ElapsedMilliseconds / 1000);
                        Stop();
                    }
                }
            } while (!quitnow && !iar.IsCompleted);
            return rv;
        }

        public override void DoRequest(Stream client)
        {
            Logging.Emit("got conenction");
            
            lock (incomingClients) {
                incomingClients.Enqueue(client);
            }
            incoming.Set();
        }

        void ServeRequestThreadFunc(object unused) {
            do {
                if (incoming.WaitOne(1000)) {
                    Stream c = null;
                    lock (incomingClients) {
                        if (incomingClients.Count > 0)
                            c = incomingClients.Dequeue();
                    }
                    if (c != null) {
                        base.DoRequest(c);
                    }
                }
            } while (!quitnow);
        }

        public override void FinishRequest(Stream clientStream)
        {
            base.FinishRequest(clientStream);
            clientStream.Close();
        }

        public override object BindStream(string cachedir)
        {
            inetlock = new Mutex(false, string.Format("cclash_inet_mtx_{0}", cachedir.GetHashCode()));
            bool held = false;
            try {
                held = inetlock.WaitOne(100);
            } catch (AbandonedMutexException) {
                Logging.Emit("new server got inet lock unexpectidly");
                held = true;
            }

            if (held) {

                int portnumber = -1;
                Settings.GetServicePort(cachedir, out portnumber);

                listener = new TcpListener(IPAddress.Loopback, portnumber);
                listener.Start(10);
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Logging.Emit("server listening on port {0}", port);
                Settings.SetServicePort(cachedir, port);
                return listener;
            } else {
                throw new CClashServerStartedException();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}