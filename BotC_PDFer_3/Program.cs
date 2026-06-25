using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualBasic.CompilerServices;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuestPDF.Drawing;
using JsonSerializer = System.Text.Json.JsonSerializer;
using SkiaSharp;




namespace BotC_PDFer_3
{
    public class Program
    {
        private static readonly HttpClient _client = new HttpClient();
        private static string _outputName = string.Empty;
        private static Dictionary<string, object> _meta = new();
        private static string _workFolder = Path.Combine(Directory.GetCurrentDirectory(), "outputs");
        private static string _config = "data/config.txt";
        private static readonly object _fileLock = new object();

        private static readonly string FullGameJsonUrl =
            "https://raw.githubusercontent.com/Fabulist-cat/Blood-on-the-CLocktower-JSON-to-PDF/refs/heads/master/data/full_game.json";

        private static readonly string JinxJsonUrl =
            "https://raw.githubusercontent.com/Fabulist-cat/Blood-on-the-CLocktower-JSON-to-PDF/refs/heads/master/data/jinx.json";

        private static readonly string NightOrderUrl =
            "https://raw.githubusercontent.com/ThePandemoniumInstitute/botc-release/refs/heads/main/resources/data/nightsheet.json";

        private static readonly string FullGameLocalPath = "data/full_game.json";
        private static readonly string JinxLocalPath = "data/jinx.json";
        private static readonly string NightOrderLocalPath = "data/night_order.json";
        private static List<Dictionary<string, string>> jinxPairs = new();

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            
            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapControllers();
            DateTime lastHeartbeat = DateTime.UtcNow;

            app.MapGet("/api/connect", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers.Connection = "keep-alive";

                Interlocked.Increment(ref AppState.ActiveConnections);
                AppState.ClientHasConnected = true;

                try
                {
                    // 🔥 CRUCIAL FIX: Write an initial handshake line and flush it immediately.
                    // This forces Kestrel to send the headers to the browser right now.
                    await context.Response.WriteAsync(":\n\n"); 
                    await context.Response.Body.FlushAsync();

                    // Keep the connection open while the tab is alive
                    await Task.Delay(-1, context.RequestAborted);
                }
                catch (TaskCanceledException)
                {
                    // Handled automatically when tab closes or refreshes
                }
                finally
                {
                    if (Interlocked.Decrement(ref AppState.ActiveConnections) == 0 && AppState.ClientHasConnected)
                    {
                        await Task.Delay(3000); // 3-second grace period for page refreshes
                        if (Volatile.Read(ref AppState.ActiveConnections) == 0)
                        {
                            app.Lifetime.StopApplication();
                        }
                    }
                }
            });
            
            // Browser launcher and startup safety net
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var serverUrl = app.Urls.FirstOrDefault() ?? "http://localhost:5081";
                OpenBrowser(serverUrl);
    
                // Safety net: If no browser connects within 15 seconds, shut down
                Task.Run(async () =>
                {
                    await Task.Delay(15000);
                    if (!AppState.ClientHasConnected && Volatile.Read(ref AppState.ActiveConnections) == 0)
                    {
                        app.Lifetime.StopApplication();
                    }
                });
            });
            
            app.Run();
            
            // Cross-platform browser opener
            void OpenBrowser(string url)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
            }
            
        }

        // NEW: Entry point for the frontend
        public static void RunPdfGenerationProcess(string inputJson, string bootleggerTranslation)
        {
            // Reset state for new run
            _meta = new();
            _outputName = string.Empty;
            jinxPairs = new();

            if (!Directory.Exists(_workFolder))
            {
                Directory.CreateDirectory(_workFolder);
            }

            UpdateData(FullGameJsonUrl, FullGameLocalPath, "mainID");
            UpdateData(JinxJsonUrl, JinxLocalPath, "jinxID");
            UpdateData(NightOrderUrl, NightOrderLocalPath, "nightID");
            
            SwapIdsWithData(inputJson, bootleggerTranslation);
            jinxPairs = FindJinxes();
            CreatePDF();
        }

        static void CreatePDF()
        {
            string filePath = $"{_workFolder}/{_outputName}.json";
            string author = _meta["author"].ToString();
            string scriptName = _meta["name"].ToString();
            string json = File.ReadAllText(filePath);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Character[] characters = JsonSerializer.Deserialize<Character[]>(json, options);
            string imagesFolder = Path.Combine(Directory.GetCurrentDirectory(), "images");
            Directory.CreateDirectory(imagesFolder);

            var missingImages = new List<string>();

            foreach (var character in characters)
            {
                if (character.Id != "_meta")
                {
                    string localImagePath = Path.Combine(imagesFolder, Path.GetFileName(character.Image ?? ""));
                    if (string.IsNullOrWhiteSpace(character.Image))
                    {
                        if (!File.Exists(localImagePath)) missingImages.Add(character.Name);
                        character.Image = localImagePath;
                        continue;
                    }

                    string imageUrl = null;
                    if (character.Image.StartsWith("http", StringComparison.OrdinalIgnoreCase)) imageUrl = character.Image;
                    else if (character.Image.StartsWith("/build", StringComparison.OrdinalIgnoreCase)) imageUrl = "https://www.pocketgrimoire.co.uk" + character.Image;
                    else if (character.Image.StartsWith("/img", StringComparison.OrdinalIgnoreCase)) imageUrl = "https://raw.githubusercontent.com/Skateside/pocket-grimoire/main/assets" + character.Image;
                    else imageUrl = character.Image;

                    DownloadImage(imageUrl, localImagePath);
                    character.Image = localImagePath;
                    Console.WriteLine($"{character.Name} processed.");
                }
            }

            if (missingImages.Count > 0)
            {
                Console.WriteLine("Images are missing:");
                foreach (var name in missingImages) Console.WriteLine($" - {name}");
                throw new Exception("Some images are missing");
            }

            QuestPDF.Settings.License = LicenseType.Community;

            var document = QuestPDF.Fluent.Document.Create(document =>
            {
                FontManager.RegisterFont(File.OpenRead("fonts/Arlekino.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Georgia.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Georgia-Italic.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Candara.ttf"));
                FontManager.RegisterFontWithCustomName("TNR", File.OpenRead("fonts/Royal_Times_New_Roman.ttf"));
                
                document.Page(page =>
                {
                    page.DefaultTextStyle(x => x.FontFamily("TNR"));
                    page.Size(PageSizes.A4);
                    page.Margin(16);
                    page.Header().Column(column =>
                    {
                        column.Item().Text($"     {scriptName}").FontSize(18).SemiBold().FontFamily("Arlekino");
                        column.Item().Row(headersRow =>
                        {
                            headersRow.RelativeItem().Column(headCol =>
                            {
                                headCol.Item().Text($"             by {author}").FontFamily("Georgia");
                            });
                            headersRow.RelativeItem().Column(florcol =>
                            {
                                var bootlegger = characters.ToList().Find(character => character.Id == "bootlegger_uk") ?? null;
                                florcol.Item().AlignRight().Row(imgRow =>
                                {
                                    if (bootlegger != null)
                                    {
                                        if (_meta.ContainsKey("bootlegger") && _meta["bootlegger"] is object[] array && array.Length > 0)
                                        {
                                            foreach (var data in array)
                                            {
                                                imgRow.RelativeItem().AlignRight().ScaleToFit().Text($"Контрабандист: {data}")
                                                    .FontFamily("Candara").FontSize(6);
                                            }
                                        }
                                        else
                                        {
                                            imgRow.RelativeItem().AlignRight().ScaleToFit().Text("Контрабандист: у цьому сценарії є саморобні правила або персонажі")
                                                .FontFamily("Candara").FontSize(6);
                                        }
                                        imgRow.ConstantItem(15).Height(15).AlignRight().PaddingRight(-4).Image(bootlegger.Image);
                                    }

                                    foreach (var character in characters)
                                    {
                                        if (character.Team is "fabled" or "loric")
                                        {
                                            if (character.Id != "bootlegger_uk")
                                            {
                                                imgRow.ConstantItem(15).Height(15).AlignRight().PaddingRight(-4).Image(character.Image);
                                            }
                                        }
                                    }
                                });
                            });
                        });
                    });
                    page.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Column(contentColumn =>
                        {
                            contentColumn.Item().Image(Path.Combine(Directory.GetCurrentDirectory(), "data", "_townsfolk.png")).FitWidth();
                            TeamDrawing("townsfolk", contentColumn, characters);
                            contentColumn.Item().Image(Path.Combine(Directory.GetCurrentDirectory(), "data", "_outsiders.png")).FitWidth();
                            TeamDrawing("outsider", contentColumn, characters);
                            contentColumn.Item().Image(Path.Combine(Directory.GetCurrentDirectory(), "data", "_minions.png")).FitWidth();
                            TeamDrawing("minion", contentColumn, characters);
                            contentColumn.Item().Image(Path.Combine(Directory.GetCurrentDirectory(), "data", "_demons.png")).FitWidth();
                            TeamDrawing("demon", contentColumn, characters);
                        });
                    });
                    page.Footer().Column(column =>
                    {
                        column.Item().Text("*окрім першої").Italic().AlignRight().FontFamily("Georgia");
                    });
                });

                document.Page(page2 =>
                {
                    page2.DefaultTextStyle(x => x.FontFamily("TNR"));
                    page2.Size(PageSizes.A4);
                    page2.MarginLeft(10); page2.MarginRight(10);
                    page2.Header().Column(column =>
                    {
                        column.Item().Text($" {scriptName}").FontSize(18).SemiBold().FontFamily("Arlekino").AlignCenter();
                    });
                    page2.Content().Layers(layers =>
                    {
                        var duskImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_dusk.png");
                        var dawnImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_dawn.png");
                        var demonImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_demon.png");
                        var minionImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_minion.png");
                        var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "_page2.png");
                        page2.Background().Image(imagePath);
                        layers.PrimaryLayer().Row(row =>
                        {
                            string nightOrderContent = File.ReadAllText(NightOrderLocalPath);
                            var order = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(nightOrderContent);
                            row.ConstantItem(100).Column(contentColumn =>
                            {
                                List<string> forder = new();
                                if (_meta.ContainsKey("firstNight") && _meta["firstNight"] is object[] array && array.Length > 0)
                                {
                                    foreach (var dat in array) forder.Add(dat.ToString());
                                }
                                else forder = order["firstNight"];

                                foreach (string dat in forder)
                                {
                                    switch (dat)
                                    {
                                        case "dusk": NightOrderDrawing("Вечір", duskImage, null, contentColumn); break;
                                        case "minioninfo": NightOrderDrawing("Дані міньйонів", minionImage, null, contentColumn); break;
                                        case "demoninfo": NightOrderDrawing("Дані демона", demonImage, null, contentColumn); break;
                                        case "dawn": NightOrderDrawing("Ранок", dawnImage, null, contentColumn); break;
                                        default:
                                            foreach (var character in characters)
                                            {
                                                if (character.Id == dat + "_uk") NightOrderDrawing(character.Name, null, character, contentColumn);
                                            }
                                            break;
                                    }
                                }
                            });
                            row.RelativeItem();
                            row.ConstantItem(230).Column(contentColumn =>
                            {
                                contentColumn.Item().Height(95);
                                contentColumn.Item().Height(500).Column(jinxColumn =>
                                {
                                    if (jinxPairs.Count == 0) jinxColumn.Item().Height(12f, Unit.Millimetre).Text("Немає жодних").FontSize(9).FontFamily("Candara").FontColor(Colors.Grey.Lighten1).AlignCenter();
                                    else
                                    {
                                        foreach (var jinx in jinxPairs)
                                        {
                                            var image1 = Path.Combine(Directory.GetCurrentDirectory(), "images", $"{jinx["character1"]}.webp");
                                            var image2 = Path.Combine(Directory.GetCurrentDirectory(), "images", $"{jinx["character2"]}.webp");
                                            jinxColumn.Item().Height(12f, Unit.Millimetre).Row(jrow =>
                                            {
                                                jrow.ConstantItem(14, Unit.Millimetre).AlignCenter().AlignMiddle().PaddingTop(-4).Image(image1).FitArea();
                                                jrow.ConstantItem(14, Unit.Millimetre).AlignCenter().AlignMiddle().PaddingTop(-4).Image(image2).FitArea();
                                                jrow.RelativeItem().StopPaging().Text(jinx["reason"]).FontSize(6).FontFamily("Candara");
                                            });
                                        }
                                    }
                                    contentColumn.Item().Row(brow =>
                                    {
                                        brow.RelativeItem().Column(tColumn =>
                                        {
                                            if (DoesTeamExist(characters, "traveller")) TeamDrawing("traveller", tColumn, characters, true);
                                            else tColumn.Item().Text("Немає жодних").FontSize(6).FontFamily("Candara").FontColor(Colors.Grey.Lighten1);
                                        });
                                        brow.RelativeItem().Column(tColumn =>
                                        {
                                            if (DoesTeamExist(characters, "fabled"))
                                            {
                                                TeamDrawing("fabled", tColumn, characters, true);
                                                TeamDrawing("loric", tColumn, characters, true);
                                            }
                                            else tColumn.Item().Text("Немає жодних").FontSize(6).FontFamily("Candara").FontColor(Colors.Grey.Lighten1);
                                        });
                                    });
                                });
                                row.RelativeItem();
                                row.ConstantItem(100).FlipOver().Column(contentColumn =>
                                {
                                    contentColumn.Item().Height(30);
                                    List<string> oorder = new();
                                    if (_meta.ContainsKey("otherNight") && _meta["otherNight"] is object[] orray && orray.Length > 0)
                                    {
                                        foreach (var dat in orray) oorder.Add(dat.ToString());
                                    }
                                    else oorder = order["firstNight"];

                                    foreach (string odat in oorder)
                                    {
                                        switch (odat)
                                        {
                                            case "dusk": NightOrderDrawing("Вечір", duskImage, null, contentColumn); break;
                                            case "dawn": NightOrderDrawing("Ранок", dawnImage, null, contentColumn); break;
                                            default:
                                                foreach (var character in characters)
                                                {
                                                    if (character.Id == odat + "_uk") NightOrderDrawing(character.Name, null, character, contentColumn);
                                                }
                                                break;
                                        }
                                    }
                                });
                            });
                        });
                    });
                });
            });

            document.GeneratePdf($"{_workFolder}/{scriptName}.pdf");
            Console.WriteLine($"PDF Generated: {scriptName}.pdf");
        }

        private static void TeamDrawing(string team, ColumnDescriptor contentColumn, Character[] characters, bool noAbility = false)
        {
            foreach (var character in characters)
            {
                if (character.Team == team)
                {
                    contentColumn.Item().Layers(layers =>
                    {
                        layers.PrimaryLayer().Row(row =>
                        {
                            row.ConstantItem(14, Unit.Millimetre).PaddingRight(8).AlignCenter().AlignMiddle().PaddingTop(-4).Image(character.Image).FitArea();
                            if (!noAbility)
                            {
                                layers.Layer().Row(lrow =>
                                {
                                    lrow.ConstantItem(25);
                                    lrow.ConstantItem(5, Unit.Millimetre).Column(jinxColumn =>
                                    {
                                        foreach (var pair in jinxPairs)
                                        {
                                            if (character.Id == pair["character1"] + "_uk")
                                            {
                                                var image2 = Path.Combine(Directory.GetCurrentDirectory(), "images", $"{pair["character2"]}.webp");
                                                jinxColumn.Item().AlignLeft().AlignBottom().Height(3f, Unit.Millimetre).Image(image2).FitArea();
                                            }
                                        }
                                    });
                                });
                            }
                            if (noAbility) row.RelativeItem().PaddingRight(8).Text(character.Name).SemiBold().FontSize(10);
                            else row.ConstantItem(86).PaddingRight(8).Text(character.Name).SemiBold().FontSize(10);
                            
                            if (!noAbility) row.RelativeItem().Text(character.Ability).FontSize(9).FontFamily("Candara");
                        });
                    });
                }
            }
        }

        static void SwapIdsWithData(string inputRaw, string bootleggerTranslation)
        {
            string dataJson = File.ReadAllText(FullGameLocalPath);
            var data = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(dataJson);
            var dataDict = new Dictionary<string, Dictionary<string, object>>();
            foreach (var entry in data)
            {
                if (entry.TryGetValue("id", out object idValue)) dataDict[idValue.ToString()] = entry;
            }

            string inputJson;
            if (Uri.TryCreate(inputRaw, UriKind.Absolute, out Uri inputUri) && (inputUri.Scheme == Uri.UriSchemeHttp || inputUri.Scheme == Uri.UriSchemeHttps))
            {
                using (var client = new WebClient()) inputJson = client.DownloadString(inputUri);
            }
            else inputJson = inputRaw;

            inputJson = inputJson.TrimStart('\uFEFF', '\u200B', '\u0000');
            JArray inputData = JsonConvert.DeserializeObject<JArray>(inputJson);

            foreach (var item in inputData)
            {
                if (item is JObject jObject && jObject.TryGetValue("id", out JToken idToken) && idToken.ToString() == "_meta")
                {
                    _outputName = jObject.GetValue("name")?.ToString() ?? "";
                    _meta["name"] = _outputName;
                    _meta["author"] = jObject.GetValue("author")?.ToString() ?? "";
                    _meta["logo"] = jObject.GetValue("logo")?.ToString() ?? "";
                    _meta["background"] = jObject.GetValue("background")?.ToString() ?? "";
                    _meta["almanac"] = jObject.GetValue("almanac")?.ToString() ?? "";
                    _meta["firstNight"] = jObject.GetValue("firstNight")?.ToArray() ?? [];
                    _meta["otherNight"] = jObject.GetValue("otherNight")?.ToArray() ?? [];
                    
                    if (!string.IsNullOrEmpty(bootleggerTranslation)) _meta["bootlegger"] = new object[] { bootleggerTranslation };
                    else _meta["bootlegger"] = jObject.GetValue("bootlegger")?.ToArray() ?? [];
                }
            }

            if (string.IsNullOrEmpty(_outputName)) throw new Exception("No script name detected");

            var outputData = new List<object>();
            foreach (var item in inputData)
            {
                if (item.Type == JTokenType.String)
                {
                    string formattedId = item.ToString().Replace("_", "").ToLower() + "_uk";
                    if (dataDict.TryGetValue(formattedId, out var dictEntry)) outputData.Add(dictEntry);
                    continue;
                }
                if (item.Type == JTokenType.Object)
                {
                    var jObj = (JObject)item;
                    if (jObj.TryGetValue("id", out var idTok) && idTok.ToString() == "_meta") { outputData.Add(jObj); continue; }
                    if (jObj.ContainsKey("team") || jObj.ContainsKey("ability")) { outputData.Add(jObj); continue; }
                    if (jObj.TryGetValue("id", out idTok))
                    {
                        string formattedId = idTok.ToString().Replace("_", "").ToLower() + "_uk";
                        if (dataDict.TryGetValue(formattedId, out var dictEntry)) outputData.Add(dictEntry);
                        continue;
                    }
                    outputData.Add(jObj);
                    continue;
                }
                outputData.Add(item);
            }
            File.WriteAllText($"{_workFolder}/{_outputName}.json", JsonConvert.SerializeObject(outputData, Formatting.Indented));
            Console.WriteLine("JSON Created");
        }

        private static void UpdateData(string link, string database, string key)
        {
            string currTag = GetSetConfig(key)?.ToString() ?? "";
            string netTag = GetFileHashTag(link);
            if (currTag != netTag)
            {
                using (var client = new WebClient()) client.DownloadFile(link, database);
                GetSetConfig(key, false, netTag);
            }
        }

        private static object? GetSetConfig(string key, bool isGet = true, object? value = null)
        {
            if (isGet)
            {
                if (!File.Exists(_config)) return null;
                string? targetLine = File.ReadLines(_config).FirstOrDefault(line => line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
                if (targetLine == null) return null;
                string rawValue = targetLine.Substring(key.Length + 1);
                if (int.TryParse(rawValue, out int intResult)) return intResult;
                if (bool.TryParse(rawValue, out bool boolResult)) return boolResult;
                return rawValue;
            }
            else
            {
                if (value == null) return null;
                string newLine = $"{key}={value.ToString()?.Trim()}";
                if (File.Exists(_config))
                {
                    var lines = File.ReadAllLines(_config).ToList();
                    int idx = lines.FindIndex(line => line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
                    if (idx != -1) lines[idx] = newLine; else lines.Add(newLine);
                    File.WriteAllLines(_config, lines);
                }
                else File.WriteAllLines(_config, new[] { newLine });
                return null;
            }
        }

        private static string? GetFileHashTag(string rawFileUrl)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, rawFileUrl);
            try
            {
                using HttpResponseMessage response = _client.SendAsync(request).Result;
                if (response.IsSuccessStatusCode && response.Headers.ETag != null) return response.Headers.ETag.Tag;
            }
            catch { }
            return null;
        }

        public static void DownloadImage(string imageUrl, string filePath)
        {
            using var client = new HttpClient();
            var response = client.GetAsync(imageUrl).Result;
            response.EnsureSuccessStatusCode();
            var imageBytes = response.Content.ReadAsByteArrayAsync().Result;
            using var sourceBitmap = SKBitmap.Decode(imageBytes);
            int cropHeight = (sourceBitmap.Height * 25) / 200;
            int top = cropHeight - (cropHeight / 4);
            int height = sourceBitmap.Height - (cropHeight * 2);
            var srcRect = new SKRectI(0, top, sourceBitmap.Width, top + height);
            var destRect = new SKRect(0, 0, sourceBitmap.Width, height);
            using var cropped = new SKBitmap(sourceBitmap.Width, height);
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(sourceBitmap, srcRect, destRect);
            using var image = SKImage.FromBitmap(cropped);
            using var data = image.Encode(SKEncodedImageFormat.Webp, 90);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        public static void NightOrderDrawing(string text, string image, Character character, ColumnDescriptor contentColumn)
        {
            contentColumn.Item().Height(7.5f, Unit.Millimetre).Row(row =>
            {
                if (image != null) row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(image).FitArea();
                else if (character != null && character.Id != "_meta") row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(character.Image).FitArea();
                else row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4);
                row.ConstantItem(64).PaddingRight(8).Text(text).SemiBold().FontSize(9).AlignLeft();
            });
        }

        public static List<Dictionary<string, string>> FindJinxes()
        {
            string jinxJson = File.ReadAllText(JinxLocalPath);
            string json = File.ReadAllText($"{_workFolder}/{_outputName}.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Character[] characters = JsonSerializer.Deserialize<Character[]>(json, options);
            var jinxData = JsonConvert.DeserializeObject<List<JObject>>(jinxJson);
            var pairs = new List<Dictionary<string, string>>();
            var characterIdsInScript = new HashSet<string>(characters.Select(c => c.Id.Replace("_uk", "")));
            foreach (var entry in jinxData)
            {
                string characterId = entry["id"]?.ToString();
                if (characterId != null && characterIdsInScript.Contains(characterId))
                {
                    var jinxArray = entry["jinx"] as JArray;
                    if (jinxArray != null)
                    {
                        foreach (var jinxItem in jinxArray)
                        {
                            string jinxedCharacterId = jinxItem["id"]?.ToString();
                            if (jinxedCharacterId != null && characterIdsInScript.Contains(jinxedCharacterId))
                            {
                                pairs.Add(new Dictionary<string, string> { { "character1", characterId }, { "character2", jinxedCharacterId }, { "reason", jinxItem["reason"]?.ToString() } });
                            }
                        }
                    }
                }
            }
            return pairs;
        }

        private static bool DoesTeamExist(Character[] characters, string id) => characters.Any(c => c.Team == id);
    }
}
public static class AppState
{
    public static int ActiveConnections = 0;
    public static bool ClientHasConnected = false;
}