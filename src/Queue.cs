using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace NoAdZip
{
    // 탐색기에서 파일 여러 개를 골라 우클릭 동작을 하면 파일마다 프로세스가 따로 뜬다.
    // 먼저 뜬 프로세스가 대표가 되어 나머지의 인수를 파이프로 받아 한 번에 처리하게 한다.
    static class JobQueue
    {
        static string PipeName { get { return "NoAdZipQueue_" + Environment.UserName; } }
        const string MutexName = @"Local\NoAdZipQueue";
        const int IdleMs = 400;

        // 대표가 되면 모인 인수 목록(내 것 포함)을, 다른 프로세스에 넘겼으면 null 을 돌려준다.
        public static List<string[]> Join(string[] myArgs)
        {
            DateTime deadline = DateTime.Now.AddSeconds(5);
            Mutex m = new Mutex(false, MutexName);
            while (true)
            {
                bool got;
                try { got = m.WaitOne(0); }
                catch (AbandonedMutexException) { got = true; }
                if (got)
                {
                    try { return Collect(myArgs); }
                    finally { try { m.ReleaseMutex(); } catch { } }
                }
                if (Send(myArgs)) return null;
                if (DateTime.Now > deadline)
                {
                    List<string[]> alone = new List<string[]>();
                    alone.Add(myArgs);
                    return alone;
                }
                Thread.Sleep(60);
            }
        }

        static bool Send(string[] args)
        {
            try
            {
                using (NamedPipeClientStream c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    c.Connect(250);
                    byte[] b = Encoding.UTF8.GetBytes(string.Join("\n", args));
                    c.Write(b, 0, b.Length);
                    c.Flush();
                }
                return true;
            }
            catch { return false; }
        }

        static List<string[]> Collect(string[] myArgs)
        {
            List<string[]> all = new List<string[]>();
            all.Add(myArgs);
            object lk = new object();
            DateTime last = DateTime.Now;
            bool stop = false;
            Thread t = new Thread(delegate ()
            {
                while (!stop)
                {
                    try
                    {
                        using (NamedPipeServerStream s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                        {
                            s.WaitForConnection();
                            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            {
                                string msg = r.ReadToEnd();
                                if (msg.Length > 0 && !stop)
                                {
                                    lock (lk) { all.Add(msg.Split('\n')); last = DateTime.Now; }
                                }
                            }
                        }
                    }
                    catch { Thread.Sleep(20); }
                }
            });
            t.IsBackground = true;
            t.Start();
            while (true)
            {
                DateTime l;
                lock (lk) l = last;
                if ((DateTime.Now - l).TotalMilliseconds >= IdleMs) break;
                Thread.Sleep(30);
            }
            stop = true;
            // 막혀 있는 WaitForConnection 을 풀어 준다.
            try
            {
                using (NamedPipeClientStream c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    c.Connect(300);
            }
            catch { }
            t.Join(1000);
            lock (lk) return new List<string[]>(all);
        }
    }
}
