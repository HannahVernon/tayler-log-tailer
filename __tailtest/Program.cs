using System.Text;
using TaylerLogTailer.Models;
using TaylerLogTailer.Services;

string dir = Path.Combine(Path.GetTempPath(), "tailtest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);

var received = new List<LogRow>();
var gate = new object();

string a = Path.Combine(dir, "a.log");
File.WriteAllText(a, "a1\na2\na3\na4\na5\n", new UTF8Encoding(false));

var tailer = new FolderTailer(dir, "*.log", recursive: false, initialLines: 2);
tailer.LinesArrived += rows =>
{
    lock (gate) received.AddRange(rows);
};
tailer.Start();

Thread.Sleep(400);

using (var fs = new FileStream(a, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
{
    sw.Write("a6\npart");
    sw.Flush();
    Thread.Sleep(400);
    sw.Write("ial\n");
    sw.Flush();
}

string b = Path.Combine(dir, "b.log");
File.WriteAllText(b, "b1\nb2\n", new UTF8Encoding(false));

Thread.Sleep(600);
tailer.Dispose();

List<LogRow> snapshot;
lock (gate) snapshot = received.ToList();

string Got() => string.Join(" | ", snapshot.Select(r => $"{r.FileName}:{r.Text}"));
Console.WriteLine("Received: " + Got());

int failures = 0;
void Check(bool ok, string label)
{
    if (!ok) failures++;
    Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
}

Check(snapshot.Count(r => r.FileName == "a.log" && r.Text == "a4") == 1, "initial last-2 includes a4");
Check(snapshot.Count(r => r.FileName == "a.log" && r.Text == "a5") == 1, "initial last-2 includes a5");
Check(!snapshot.Any(r => r.FileName == "a.log" && r.Text == "a3"), "initial last-2 excludes a3");
Check(snapshot.Any(r => r.FileName == "a.log" && r.Text == "a6"), "appended a6 followed");
Check(snapshot.Any(r => r.FileName == "a.log" && r.Text == "partial"), "split partial line reassembled");
Check(snapshot.Any(r => r.FileName == "b.log" && r.Text == "b1"), "new file b discovered");
Check(snapshot.Any(r => r.FileName == "b.log" && r.Text == "b2"), "new file b second line");

try { Directory.Delete(dir, true); } catch { }

Environment.Exit(failures == 0 ? 0 : 1);
