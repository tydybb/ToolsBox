using System.Net;
using System.Net.Sockets;
using ToolsBox.MediaDownloads;

if (args.Length == 2 && args[0] == "--test-captured")
{
 var capturedRoot = Path.GetFullPath(args[1]);
 using var capturedTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
 return await VerifyCapturedVideoAsync(capturedRoot, capturedTimeout.Token) ? 0 : 1;
}

if (args.Length == 2 && args[0] == "--test-cookies")
{
 var cookieRoot = Path.GetFullPath(args[1]);
 var cookieManager = new ComponentManager(Path.Combine(cookieRoot, "components"));
 var cookieSample = Path.Combine(cookieRoot, "fixtures", "sample.mp4");
 using var cookieTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
 if (await cookieManager.GetInstalledAsync(cookieTimeout.Token) is null || !File.Exists(cookieSample))
 { Console.Error.WriteLine("FAIL synthetic Cookie fixture or installed components missing."); return 2; }
 return await VerifyNamelessCookiesAsync(new MediaDownloadService(cookieManager), cookieSample, Path.Combine(cookieRoot, "cookie-output"), cookieTimeout.Token) ? 0 : 1;
}

if (args.Length != 2 || args[0] != "--install-and-test")
{ Console.WriteLine("显式联网验收：--install-and-test <临时工作目录>；下载官方组件并只测试本机生成媒体。已有组件本机验收：--test-captured <工作目录>。"); return 2; }
var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
var manager = new ComponentManager(Path.Combine(root, "components"));
var progress = new Progress<DownloadProgress>(p => Console.WriteLine($"{p.Status} {p.Percent:F1} {p.Detail}"));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
var paths = await manager.GetInstalledAsync(timeout.Token) ?? await manager.InstallAsync(progress, timeout.Token);
if (!await manager.IsReadyAsync(timeout.Token)) throw new Exception("已安装的视频组件未通过环境检测。");
Console.WriteLine("PASS installed component health check");
var fixtures = Path.Combine(root, "fixtures"); Directory.CreateDirectory(fixtures);
var sample = Path.Combine(fixtures, "sample.mp4");
var conversionSample = Path.Combine(fixtures, "conversion.mkv");
if (!File.Exists(sample))
{
 var result = await ProcessRunner.RunAsync(paths.FFmpeg, ["-nostdin", "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", sample], timeout.Token);
 if (result.ExitCode != 0) throw new Exception("无法生成本地验收视频。");
}
if (!File.Exists(conversionSample))
{
 var conversion = await ProcessRunner.RunAsync(paths.FFmpeg, ["-nostdin", "-n", "-i", sample, "-c:v", "ffv1", "-c:a", "pcm_s16le", conversionSample], timeout.Token);
 if (conversion.ExitCode != 0) throw new Exception("无法生成 FFV1/PCM 转码验收视频。");
}
foreach (var (format, file) in new[] { ("hls", "sample.m3u8"), ("dash", "sample.mpd") })
{
 var result = await ProcessRunner.RunAsync(paths.FFmpeg, ["-nostdin", "-y", "-i", sample, "-c", "copy", "-f", format, Path.Combine(fixtures, file)], timeout.Token, workingDirectory: fixtures);
 if (result.ExitCode != 0) throw new Exception("无法生成 " + format);
}
foreach (var fragment in new[] { "init-stream0.m4s", "init-stream1.m4s", "chunk-stream0-00001.m4s", "chunk-stream1-00001.m4s" })
 if (!File.Exists(Path.Combine(fixtures, fragment))) throw new Exception("DASH 生成器没有在服务器目录写入分片：" + fragment);
var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
using var listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
var server = Task.Run(async () =>
{
 while (!timeout.IsCancellationRequested)
 {
  HttpListenerContext context;
  try { context = await listener.GetContextAsync().WaitAsync(timeout.Token); } catch { break; }
  try
  {
   var filename = Path.GetFileName(context.Request.Url!.AbsolutePath);
   var file = Path.Combine(fixtures, filename);
   if (!File.Exists(file)) { context.Response.StatusCode = 404; context.Response.Close(); continue; }
   context.Response.ContentType = Path.GetExtension(file) switch { ".mkv" => "video/x-matroska", ".m3u8" => "application/vnd.apple.mpegurl", ".mpd" => "application/dash+xml", ".ts" => "video/mp2t", _ => "video/mp4" };
   context.Response.ContentLength64 = new FileInfo(file).Length;
   if (context.Request.HttpMethod != "HEAD") { await using var input = File.OpenRead(file); await input.CopyToAsync(context.Response.OutputStream, timeout.Token); }
   context.Response.Close();
  }
  catch { context.Response.Abort(); }
 }
});
try
{
 var service = new MediaDownloadService(manager);
 foreach (string filename in new[] { "sample.mp4", "sample.m3u8", "sample.mpd", "conversion.mkv" })
 {
  var output = await service.DownloadVideoAsync(new Uri($"http://localhost:{port}/{filename}"), Path.Combine(root, "output"), progress: progress, ct: timeout.Token);
  if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new Exception("验收输出为空。");
  if (filename == "conversion.mkv")
  {
   var verify = await ProcessRunner.RunAsync(paths.FFprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", output], timeout.Token);
   if (verify.ExitCode != 0 || !verify.Output.Contains("h264") || !verify.Output.Contains("aac")) throw new Exception("FFV1/PCM 未转码为 H264/AAC。");
  }
  Console.WriteLine($"PASS {filename} => {output}");
 }
 var dashUri = new Uri($"http://localhost:{port}/sample.mpd");
 var inspected = await service.InspectVideoAsync(dashUri, timeout.Token);
 var selected = inspected.Formats.First();
 var selectedOutput = await service.DownloadVideoAsync(dashUri, Path.Combine(root, "output"), selected.Id, progress, timeout.Token);
 Console.WriteLine($"PASS selected DASH format {selected.Id} => {selectedOutput}");
 if (!await VerifyNamelessCookiesAsync(service, sample, Path.Combine(root, "cookie-output"), timeout.Token)) return 1;
 return 0;
}
finally { timeout.Cancel(); listener.Stop(); await server; }

static async Task<bool> VerifyNamelessCookiesAsync(MediaDownloadService service, string sample, string outputDirectory, CancellationToken ct)
{
 var tcp = new TcpListener(IPAddress.Loopback, 0);
 tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
 using var listener = new HttpListener();
 listener.Prefixes.Add($"http://127.0.0.1:{port}/");
 listener.Prefixes.Add($"http://localhost:{port}/");
 listener.Start();
 using var serverStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
 int authenticatedRequests = 0, offPathRequests = 0, offDomainRequests = 0, invalidCookieRequests = 0;
 var server = Task.Run(async () =>
 {
  while (!serverStop.IsCancellationRequested)
  {
   HttpListenerContext context;
   try { context = await listener.GetContextAsync().WaitAsync(serverStop.Token); } catch { break; }
   try
   {
    var uri = context.Request.Url!;
    bool authenticated = uri.Host == "127.0.0.1" && uri.AbsolutePath == "/cookie-auth/sample.mp4";
    bool offPath = uri.Host == "127.0.0.1" && uri.AbsolutePath == "/cookie-outside/sample.mp4";
    bool offDomain = uri.Host == "localhost" && uri.AbsolutePath == "/cookie-auth/sample.mp4";
    if (!authenticated && !offPath && !offDomain)
    { context.Response.StatusCode = 404; context.Response.Close(); continue; }
    // Inspect only synthetic cookie data in memory; never include request headers in diagnostics.
    var cookieParts = (context.Request.Headers["Cookie"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    bool validCookies = authenticated
     ? cookieParts.Length == 2 && cookieParts.Contains("fixture_session=fixture", StringComparer.Ordinal) && cookieParts.Contains("fixture_bare_token", StringComparer.Ordinal)
     : cookieParts.Length == 0;
    if (!validCookies)
    {
     Interlocked.Increment(ref invalidCookieRequests);
     context.Response.StatusCode = 403; context.Response.Close(); continue;
    }
    if (authenticated) Interlocked.Increment(ref authenticatedRequests);
    else if (offPath) Interlocked.Increment(ref offPathRequests);
    else Interlocked.Increment(ref offDomainRequests);
    context.Response.ContentType = "video/mp4";
    context.Response.ContentLength64 = new FileInfo(sample).Length;
    if (context.Request.HttpMethod != "HEAD")
    {
     await using var input = File.OpenRead(sample);
     await input.CopyToAsync(context.Response.OutputStream, serverStop.Token);
    }
    context.Response.Close();
   }
   catch { context.Response.Abort(); }
  }
 });
 var cookies = new MediaCookie[]
 {
  new("fixture_session", "fixture", "127.0.0.1", "/cookie-auth/", false, null),
  new("", "fixture_bare_token", "127.0.0.1", "/cookie-auth/", false, null),
  new("fixture_off_domain", "must_not_send", "example.invalid", "/", false, null),
  new("fixture_off_path", "must_not_send", "127.0.0.1", "/cookie-excluded/", false, null)
 };
 int formatCount = -1;
 string phase = "inspection request";
 try
 {
  var authenticatedUri = new Uri($"http://127.0.0.1:{port}/cookie-auth/sample.mp4");
  var info = await service.InspectVideoAsync(authenticatedUri, ct, cookies);
  phase = "inspection assertion";
  formatCount = info.Formats.Count;
  int inspectedRequests = Volatile.Read(ref authenticatedRequests);
  // Direct MP4 inspection may expose no selectable formats; the authenticated request proves cookie loading.
  if (inspectedRequests == 0 || Volatile.Read(ref invalidCookieRequests) != 0)
   throw new InvalidOperationException();
  Console.WriteLine("PASS nameless Cookie loaded by yt-dlp inspection with exact named and bare values");

  phase = "download request";
  var output = await service.DownloadVideoAsync(authenticatedUri, outputDirectory, ct: ct, cookies: cookies);
  phase = "download assertion";
  if (!File.Exists(output) || new FileInfo(output).Length == 0 || Volatile.Read(ref authenticatedRequests) <= inspectedRequests || Volatile.Read(ref invalidCookieRequests) != 0)
   throw new InvalidOperationException();
  Console.WriteLine("PASS nameless Cookie authenticated actual yt-dlp media download");

  phase = "path scope request";
  await service.InspectVideoAsync(new Uri($"http://127.0.0.1:{port}/cookie-outside/sample.mp4"), ct, cookies);
  phase = "path scope assertion";
  if (Volatile.Read(ref offPathRequests) == 0 || Volatile.Read(ref invalidCookieRequests) != 0)
   throw new InvalidOperationException();
  Console.WriteLine("PASS nameless and named Cookie values excluded outside their path");

  phase = "domain scope request";
  await service.InspectVideoAsync(new Uri($"http://localhost:{port}/cookie-auth/sample.mp4"), ct, cookies);
  phase = "domain scope assertion";
  if (Volatile.Read(ref offDomainRequests) == 0 || Volatile.Read(ref invalidCookieRequests) != 0)
   throw new InvalidOperationException();
  Console.WriteLine("PASS nameless and named Cookie values excluded outside their host");
  return true;
 }
 catch (Exception ex)
 {
  Console.Error.WriteLine($"FAIL synthetic nameless Cookie {phase} check. Type={ex.GetType().Name}; formats={formatCount}; authenticated={Volatile.Read(ref authenticatedRequests)}; invalid={Volatile.Read(ref invalidCookieRequests)}; offPath={Volatile.Read(ref offPathRequests)}; offDomain={Volatile.Read(ref offDomainRequests)}.");
  return false;
 }
 finally { serverStop.Cancel(); listener.Stop(); await server; }
}

static async Task<bool> VerifyCapturedVideoAsync(string root, CancellationToken ct)
{
 var manager = new ComponentManager(Path.Combine(root, "components"));
 var paths = await manager.GetInstalledAsync(ct);
 if (paths is null) { Console.Error.WriteLine("FAIL captured fixture requires existing components; no installation attempted."); return false; }
 var fixtureRoot = Path.Combine(root, "captured-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
 Directory.CreateDirectory(fixtureRoot);
 var source = Path.Combine(fixtureRoot, "source.webm");
 var generated = await ProcessRunner.RunAsync(paths.FFmpeg,
  ["-nostdin", "-n", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
   "-t", "4", "-c:v", "libvpx", "-pix_fmt", "yuv420p", "-c:a", "libvorbis", source], ct);
 if (generated.ExitCode != 0) { Console.Error.WriteLine("FAIL cannot generate synthetic VP8/Vorbis WEBM."); return false; }
 var sourceProbe = await ProcessRunner.RunAsync(paths.FFprobe,
  ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", source], ct);
 if (sourceProbe.ExitCode != 0 || !sourceProbe.Output.Contains("vp8") || !sourceProbe.Output.Contains("vorbis"))
 { Console.Error.WriteLine("FAIL source is not VP8/Vorbis."); return false; }
 var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
 using var listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
 using var serverStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
 var server = Task.Run(async () =>
 {
  while (!serverStop.IsCancellationRequested)
  {
   HttpListenerContext context;
   try { context = await listener.GetContextAsync().WaitAsync(serverStop.Token); } catch { break; }
   try
   {
    if (context.Request.Url?.AbsolutePath != "/source.webm") { context.Response.StatusCode = 404; context.Response.Close(); continue; }
    context.Response.ContentType = "video/webm";
    context.Response.ContentLength64 = new FileInfo(source).Length;
    await using var input = File.OpenRead(source);
    await input.CopyToAsync(context.Response.OutputStream, serverStop.Token);
    context.Response.Close();
   }
   catch { context.Response.Abort(); }
  }
 });
 try
 {
  var service = new MediaDownloadService(manager);
  var uri = new Uri($"http://localhost:{port}/source.webm");
  var outputDirectory = Path.Combine(fixtureRoot, "output");
  var first = await service.DownloadCapturedVideoAsync(uri, outputDirectory, "captured-transcode", 4, ct: ct);
  var firstHash = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(first, ct));
  var second = await service.DownloadCapturedVideoAsync(uri, outputDirectory, "captured-transcode", 4, ct: ct);
  if (first == second || !firstHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(first, ct))))
   throw new InvalidOperationException("An existing output was replaced.");
  foreach (var output in new[] { first, second })
  {
   var probe = await ProcessRunner.RunAsync(paths.FFprobe,
    ["-v", "error", "-show_entries", "format=format_name,duration:stream=codec_type,codec_name", "-of", "json", output], ct);
   if (probe.ExitCode != 0) throw new InvalidOperationException("MP4 probe failed.");
   new VideoInfo("captured-transcode", [], 4, true).ValidateOutput(probe.Output);
   using var json = System.Text.Json.JsonDocument.Parse(probe.Output);
   var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
   if (!streams.Any(s => s.GetProperty("codec_name").GetString() == "h264") ||
       !streams.Any(s => s.GetProperty("codec_name").GetString() == "aac")) throw new InvalidOperationException("Output codecs are not H264/AAC.");
   double duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
   if (Math.Abs(duration - 4) > 0.3) throw new InvalidOperationException("Output duration differs from synthetic four-second source.");
   Console.WriteLine($"PASS captured VP8/Vorbis => MP4 H264/AAC; duration={duration:F3}s; output={output}");
  }
  if (Directory.GetDirectories(outputDirectory, ".video-*").Length != 0) throw new InvalidOperationException("Owned staging directory remains.");
  Console.WriteLine("PASS same-name captured outputs remain distinct; original SHA256 unchanged; owned staging cleaned.");
  return true;
 }
 catch (Exception error)
 { Console.Error.WriteLine("FAIL captured local media validation. Type=" + error.GetType().Name); return false; }
 finally { serverStop.Cancel(); listener.Stop(); await server; }
}
