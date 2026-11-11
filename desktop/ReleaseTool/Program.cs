using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length != 2) throw new ArgumentException("Usage: ReleaseTool <private-key.pem> <repository-root>");
var privateKeyPath = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
var desktop = Path.Combine(root, "desktop");
var publicSite = Path.Combine(root, "cloud", "public");
var version = XDocument.Load(Path.Combine(desktop, "BatteryWatch.csproj")).Descendants("Version").Single().Value;
if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("Version must have three numeric parts");

var architectures = new[] { "win-x64", "win-arm64", "win-x86" };
var hashes = architectures.ToDictionary(arch => arch, arch => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(publicSite, "downloads", arch, "BatteryWatch.exe")))));
using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
static string Normalise(string pem) => string.Concat(pem.Where(c => !char.IsWhiteSpace(c)));
if (Normalise(rsa.ExportSubjectPublicKeyInfoPem()) != Normalise(File.ReadAllText(Path.Combine(desktop, "update-public.pem"))))
    throw new CryptographicException("Release key does not match the app's embedded public key");
var canonical = version + "\n" + string.Join("", architectures.Select(arch => $"{arch}:{hashes[arch]}\n"));
var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
var manifest = JsonSerializer.Serialize(new { version, sha256 = hashes, signature }, new JsonSerializerOptions { WriteIndented = true });
var manifestPath = Path.Combine(publicSite, "updates", "latest.json");
Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

var pagePath = Path.Combine(publicSite, "download.html");
var page = File.ReadAllText(pagePath);
foreach (var arch in architectures)
{
    var expression = new Regex($"(href=\"/downloads/{arch}/BatteryWatch\\.exe\"[\\s\\S]*?SHA-256: <code>)[0-9A-F]{{64}}", RegexOptions.CultureInvariant);
    if (expression.Matches(page).Count != 1) throw new InvalidDataException($"Could not find exactly one checksum for {arch} on the download page");
    page = expression.Replace(page, match => match.Groups[1].Value + hashes[arch], 1);
}
File.WriteAllText(pagePath, page);
File.WriteAllText(manifestPath, manifest + "\n");
Console.WriteLine($"Signed Battery Watch {version} for {string.Join(", ", architectures)}");
