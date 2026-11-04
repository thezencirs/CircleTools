using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Güncelleyici testi: yerel sahte "GitHub" sunucusu + gerçek Updater.cs
static class UpdaterTest
{
    static int fails;
    static void Check(bool c, string m) { Console.WriteLine((c ? "OK   " : "FAIL ") + m); if (!c) fails++; }

    static Dictionary<string, byte[]> routes = new Dictionary<string, byte[]>();
    static string Sha(byte[] b) { using (SHA256 s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static string Sha(string p) { return Sha(File.ReadAllBytes(p)); }

    static void Serve(TcpListener l)
    {
        while (true)
        {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                using (c)
                {
                    try
                    {
                        NetworkStream ns = c.GetStream();
                        StreamReader r = new StreamReader(ns, Encoding.ASCII, false, 4096, true);
                        string first = r.ReadLine();
                        string line; while (!string.IsNullOrEmpty(line = r.ReadLine())) { }
                        string path = first.Split(' ')[1];
                        byte[] body; int code = 200;
                        if (!routes.TryGetValue(path, out body)) { code = 404; body = Encoding.UTF8.GetBytes("{\"message\":\"Not Found\"}"); }
                        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Not Found") + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        ns.Write(head, 0, head.Length); ns.Write(body, 0, body.Length); ns.Flush();
                    }
                    catch { }
                }
            });
        }
    }

    static string Json(string baseUrl, string tag, bool withSums)
    {
        string zipUrl = baseUrl + "/dl/CircleTools-" + tag + "-win.zip", sumsUrl = baseUrl + "/dl/SHA256SUMS.txt";
        return "{\"tag_name\":\"" + tag + "\",\"body\":\"Yeni \\u00f6zellik\\r\\n- madde \\\"bir\\\"\",\"assets\":[" +
               "{\"name\":\"z\",\"uploader\":{\"login\":\"x\"},\"browser_download_url\":\"" + zipUrl + "\"}" +
               (withSums ? ",{\"name\":\"s\",\"uploader\":{\"login\":\"x\"},\"browser_download_url\":\"" + sumsUrl + "\"}" : "") + "]}";
    }

    static int Main(string[] a)
    {
        string realExe = a[0];
        TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        string baseUrl = "http://127.0.0.1:" + port;
        new Thread(delegate () { Serve(l); }) { IsBackground = true }.Start();
        Updater.ApiBase = baseUrl;

        // sahte yayın zip'i (içinde gerçek CircleTools.exe)
        string work = Path.Combine(Path.GetTempPath(), "upd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string stage = Path.Combine(work, "stage"); Directory.CreateDirectory(stage);
        File.Copy(realExe, Path.Combine(stage, "CircleTools.exe"));
        File.WriteAllText(Path.Combine(stage, "LICENSE"), "MIT");
        string zipPath = Path.Combine(work, "rel.zip");
        ZipFile.CreateFromDirectory(stage, zipPath);
        byte[] zipBytes = File.ReadAllBytes(zipPath);
        string zipName = "CircleTools-v9.9.9-win.zip";
        routes["/dl/" + zipName] = zipBytes;
        routes["/dl/SHA256SUMS.txt"] = Encoding.ASCII.GetBytes(Sha(zipBytes) + "  " + zipName + "\n" + Sha(realExe) + "  CircleTools.exe\n");

        // 1) Fetch
        Updater.Repo = "test/CircleTools";
        routes["/repos/test/CircleTools/releases/latest"] = Encoding.UTF8.GetBytes(Json(baseUrl, "v9.9.9", true));
        string err; Updater.Release r = Updater.Fetch(out err);
        Check(r != null && err == null, "Fetch: yanit okundu" + (err != null ? " (" + err + ")" : ""));
        Check(r != null && r.Version == new Version(9, 9, 9), "Fetch: surum 9.9.9 ayrildi");
        Check(r != null && r.ZipUrl != null && r.ZipUrl.EndsWith(zipName) && r.SumsUrl != null && r.SumsUrl.EndsWith("SHA256SUMS.txt"), "Fetch: zip ve SHA256SUMS adresleri bulundu");
        Check(r != null && r.Notes.Contains("özellik") && r.Notes.Contains("\"bir\"") && r.Notes.Contains("\n"), "Fetch: surum notlari (unicode/tirnak/satir) dogru cozuldu");
        Check(Updater.IsNewer(r), "IsNewer: 9.9.9 > mevcut " + Updater.CurrentText);

        // 2) 404
        Updater.Repo = "yok/yok";
        Updater.Release r404 = Updater.Fetch(out err);
        Check(r404 == null && err != null && err.Contains("bulunamad"), "Fetch 404: anlasilir hata -> '" + err + "'");

        // 3) sunucuya ulasilamiyor
        Updater.ApiBase = "http://127.0.0.1:1";
        Updater.Release rx = Updater.Fetch(out err);
        Check(rx == null && err != null, "Fetch: baglanti yok -> hata veriyor, cokmuyor");
        Updater.ApiBase = baseUrl; Updater.Repo = "test/CircleTools";

        // Apply testleri ayri bir "uygulama" kopyasi uzerinde yapilir (Application.ExecutablePath = bu test exe'si)
        string self = System.Windows.Forms.Application.ExecutablePath;
        string selfHash = Sha(self);

        // 4) bozuk ozet
        routes["/dl/SHA256SUMS.txt"] = Encoding.ASCII.GetBytes(new string('0', 64) + "  " + zipName + "\n");
        bool ok = Updater.Apply(r, out err);
        Check(!ok && err != null && err.Contains("SHA256"), "Apply: yanlis SHA256 -> reddedildi: " + err);
        Check(Sha(self) == selfHash && !File.Exists(self + ".old"), "Apply: reddedilince mevcut exe degismedi");

        // 5) ozet dosyasinda zip yok
        routes["/dl/SHA256SUMS.txt"] = Encoding.ASCII.GetBytes(Sha(zipBytes) + "  baska.zip\n");
        ok = Updater.Apply(r, out err);
        Check(!ok && err != null, "Apply: ozet dosyasinda zip yoksa reddedildi: " + err);

        // 6) ozet asset'i hic yok
        Updater.Release noSums = new Updater.Release { Version = r.Version, Tag = r.Tag, ZipUrl = r.ZipUrl, SumsUrl = null };
        ok = Updater.Apply(noSums, out err);
        Check(!ok && err != null, "Apply: SHA256SUMS yoksa guvenlik geregi reddedildi: " + err);

        // 7) dogru guncelleme
        routes["/dl/SHA256SUMS.txt"] = Encoding.ASCII.GetBytes(Sha(zipBytes) + "  " + zipName + "\n" + Sha(realExe) + "  CircleTools.exe\n");
        ok = Updater.Apply(r, out err);
        Check(ok && err == null, "Apply: dogru paket guncellendi" + (err != null ? " (" + err + ")" : ""));
        Check(Sha(self) == Sha(realExe), "Apply: exe yeni surumle degisti (hash eslesiyor)");
        Check(File.Exists(self + ".old"), "Apply: eski surum .old olarak yedeklendi");
        System.Threading.Thread.Sleep(1500);
        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("UpdaterTest"))
        {
            try { if (string.Equals(p.MainModule.FileName, self, StringComparison.OrdinalIgnoreCase) && p.Id != System.Diagnostics.Process.GetCurrentProcess().Id) { Console.WriteLine("     (yeni surum baslatildi, pid " + p.Id + ")"); p.Kill(); } } catch { }
        }

        Console.WriteLine(fails == 0 ? "TUM TESTLER GECTI" : fails + " TEST BASARISIZ");
        return fails;
    }
}
