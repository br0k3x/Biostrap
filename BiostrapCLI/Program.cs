using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BiostrapCLI
{
    internal class Program
    {
        static string logoAscii = @"
                    +                                      
                   = =====++                               
                  ===.===========++                        
                  =-:=.=======+++++++++++#                 
                 ===::+:+++++++++++++++++++++++            
                 +:+:+++=++++++++++++++++++++++            
                ++=+:+++++++++++++++++++++++++             
                +::++++++++ ++++++++++++++++++             
               +:+++++++++        #++++++++++              
               ++++++++++*        +++++++++**              
              +::+:++++++        +**********               
              +##*++++++*        +**********               
             +**+#***********++ :**********                
             *###****************.*********                
            +#*##***************** ********                
            ***#******************* ******                 
                  ****************** ****                  
                        ************* ***                  
                               *******.*#                  
                                      *:                     
";

        static string clientVersionURL = "https://clientsettings.roblox.com/v2/client-version/WindowsPlayer/channel/LIVE";
        static string biostrapFolder = "%localappdata%/br0k3x/Biostrap/";

        public class Root
        {
            public string clientVersionUpload { get; set; }
            public string version { get; set; }
            public string bootstrapperVersion { get; set; }
        }

        static async Task DownloadFileAsync(string url, string destinationPath)
        {
            using HttpClient client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync();
            await using FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await contentStream.CopyToAsync(fileStream);
        }

        static void SafeExtractZip(string zipPath, string extractFolder)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                string fullExtractPath = Path.GetFullPath(extractFolder);

                foreach (var entry in archive.Entries)
                {
                    // Normalize entry path: replace forward slashes with OS separator, trim starting separators
                    string normalizedEntryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

                    string destinationPath = Path.GetFullPath(Path.Combine(fullExtractPath, normalizedEntryPath));

                    // Ensure the destination path is within the target directory
                    if (!destinationPath.StartsWith(fullExtractPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"Entry is outside the target dir: {entry.FullName}");
                    }

                    // Skip empty directories
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    // Create parent directory if not exists
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                    // Extract file with overwrite
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine(logoAscii);
            Console.WriteLine("biostrap client CLI");

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "BiostrapCLI 0.1");

            if (args.Length == 0)
            {
                Console.WriteLine("Couldn't detect any arguments provided.. exiting");
                return;
            }

            Console.WriteLine($"Argument: {args[0]}");

            switch (args[0])
            {
                case "install":
                    {
                        HttpResponseMessage response = await client.GetAsync(clientVersionURL);
                        response.EnsureSuccessStatusCode();

                        string json = await response.Content.ReadAsStringAsync();

                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        Root result = JsonSerializer.Deserialize<Root>(json, options);
                        string version = result.clientVersionUpload;
                        string baseUrl = "https://setup-aws.rbxcdn.com";
                        string blobDir = "/";
                        string versionPath = $"{baseUrl}{blobDir}{version}-";

                        string localVersionFolder = Path.Combine(
                            Environment.ExpandEnvironmentVariables(biostrapFolder),
                            "versions", version
                        );

                        if (!Directory.Exists(localVersionFolder))
                        {
                            Directory.CreateDirectory(localVersionFolder);
                        }

                        Console.WriteLine($"Fetching manifest from: {versionPath}rbxPkgManifest.txt");

                        string manifestUrl = versionPath + "rbxPkgManifest.txt";
                        string manifestContent;

                        try
                        {
                            manifestContent = await client.GetStringAsync(manifestUrl);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to fetch manifest: {ex.Message}");
                            return;
                        }

                        var zipFiles = manifestContent
                            .Split('\n')
                            .Select(line => line.Trim())
                            .Where(line => line.EndsWith(".zip"))
                            .ToList();

                        foreach (var zipFile in zipFiles)
                        {
                            string fileUrl = versionPath + zipFile;

                            // Create extraction folder based on zip filename (without .zip)
                            string zipNameWithoutExt = Path.GetFileNameWithoutExtension(zipFile);
                            string[] folderParts = zipNameWithoutExt.Split('-', StringSplitOptions.RemoveEmptyEntries);

                            string extractionFolder = localVersionFolder;
                            foreach (var part in folderParts)
                            {
                                extractionFolder = Path.Combine(extractionFolder, part);
                            }

                            if (!Directory.Exists(extractionFolder))
                            {
                                Directory.CreateDirectory(extractionFolder);
                            }

                            string localZipPath = Path.Combine(extractionFolder, zipFile);

                            Console.WriteLine($"Downloading {zipFile}...");
                            try
                            {
                                await DownloadFileAsync(fileUrl, localZipPath);
                                Console.WriteLine($"✓ Downloaded {zipFile}");

                                // Extract zip safely into its folder
                                Console.WriteLine($"Extracting {zipFile} to {extractionFolder}...");
                                SafeExtractZip(localZipPath, extractionFolder);
                                Console.WriteLine($"✓ Extracted {zipFile}");

                                // Delete zip after extraction
                                File.Delete(localZipPath);
                                Console.WriteLine($"Deleted {zipFile}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"✗ Error with {zipFile}: {ex.Message}");
                            }
                        }

                        Console.WriteLine($"\nAll files downloaded, extracted and cleaned up at: {localVersionFolder}");
                        break;
                    }
                default:
                    {
                        Console.WriteLine("Invalid argument. Valid arguments: install");
                        break;
                    }
            }
        }
    }
}
