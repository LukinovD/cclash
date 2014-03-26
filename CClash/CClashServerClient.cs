﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Web.Script.Serialization;
using System.Diagnostics;

namespace CClash
{
    public sealed class CClashServerClient : ICompilerCache
    {
        NamedPipeClientStream ncs;
        string pipename = null;

        public CClashServerClient(string cachedir)
        {
            pipename = CClashServer.MakePipeName(cachedir);
        }


        void Open()
        {      
            ncs = new NamedPipeClientStream(".", pipename, PipeDirection.InOut);
        }

        void Connect()
        {
            Logging.Emit("connecting to service...");
            var exe = GetType().Assembly.Location;
            if (ncs == null)
                Open();

            for (int i = 0; i < 2; i++)
            {
                try {
                    if (!ncs.IsConnected)
                        ncs.Connect(100);
                    ncs.ReadMode = PipeTransmissionMode.Message;
                    return;
                } catch (IOException ex) {
                    Logging.Emit("error connecting {0}", ex.Message);
                    try { ncs.Dispose(); Open(); } catch { }
                } catch (TimeoutException) {
                }
            }
            throw new CClashWarningException("failed to connect to server");
        }

        public ICacheInfo Stats
        {
            get
            {
                return null;
            }
        }

        public bool IsSupported(IEnumerable<string> args)
        {
            return true;
        }

        string compilerPath;
        string workingdir;
        Dictionary<string, string> environment;

        public void SetCompiler(string compiler, string workdir, System.Collections.Generic.Dictionary<string,string> envs )
        {
            if (string.IsNullOrEmpty(compiler)) throw new ArgumentNullException("compiler");
            if (string.IsNullOrEmpty(workdir)) throw new ArgumentNullException("workdir");
            environment = envs;
            workingdir = workdir;
            compilerPath = System.IO.Path.GetFullPath(compiler);
            Connect();
        }

        public int CompileOrCache(IEnumerable<string> args)
        {
            var req = new CClashRequest()
            {
                cmd = Command.Run,
                compiler = compilerPath,
                envs = environment,
                workdir = workingdir,
                argv = new List<string> ( args ),
            };
            var resp = Transact(req);
            if (resp != null)
            {

                Console.Error.Write(resp.stderr);
                Console.Out.Write(resp.stdout);

                return resp.exitcode;
            }

            return -1;
            
        }

        public CClashResponse Transact(CClashRequest req)
        {
            Connect();
            CClashResponse resp = null;

            var txbuf = req.Serialize();

            ncs.Write(txbuf, 0, txbuf.Length);
            ncs.Flush();

            var rx = new List<byte>();

            var rxbuf = new byte[8192];
            do
            {
                var rbytes = ncs.Read(rxbuf, 0, rxbuf.Length);
                rx.AddRange(rxbuf.Take(rbytes));
            } while (!ncs.IsMessageComplete);

            if (rx.Count > 0)
            {
                resp = CClashMessage.Deserialize<CClashResponse>(rx.ToArray());
                ncs.Close();
            }
            return resp;
        }

        public DataHash DeriveHashKey(IEnumerable<string> args)
        {
            throw new NotSupportedException();
        }

        public string GetStats(string compiler)
        {
            var req = new CClashRequest()
            {
                cmd = Command.GetStats,
                compiler = compiler,
            };

            var resp = Transact(req);
            return resp.stdout;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                ncs.Dispose();
            }
        }
    }
}
