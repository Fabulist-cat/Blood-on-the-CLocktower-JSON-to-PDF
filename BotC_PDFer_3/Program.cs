using System.Net;
using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;
using System.Data.Common;
using QuestPDF.Drawing;
using System.Net;
using System.Globalization;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BotC_PDFer_3;
using SkiaSharp;

/*
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(); 
var app = builder.Build();
app.UseDefaultFiles(); 
app.UseStaticFiles();  
app.UseRouting();
app.MapHub<AppHub>("/botc_pdfer_3");
app.Run();
*/

namespace BotC_PDFer_3
{
    internal class Program
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


        private static void Main(string[] args)
        {
            if (!Directory.Exists(_workFolder))
            {
                Directory.CreateDirectory(_workFolder);
            }
            
            
            UpdateData(FullGameJsonUrl, FullGameLocalPath, "mainID");
            UpdateData(JinxJsonUrl, JinxLocalPath, "jinxID");
            UpdateData(NightOrderUrl, NightOrderLocalPath, "nightID");
            SwapIdsWithData();
            jinxPairs = FindJinxes();
            CreatePDF();

        }

        static void CreatePDF()
        {
            string filePath = $"{_workFolder}/{_outputName}.json";
            string author = _meta["author"].ToString();
            string scriptName = _meta["name"].ToString();
            string json = File.ReadAllText(filePath);
            
            // Deserialize the JSON data
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            Character[] characters = JsonSerializer.Deserialize<Character[]>(json, options);
            string imagesFolder = Path.Combine(Directory.GetCurrentDirectory(), "images");
            Directory.CreateDirectory(imagesFolder);
            
            var missingImages = new List<string>();
            
            foreach (var character in characters)
            {
                if(character.Id != "_meta")
                // Determine local image path
                {
                    string localImagePath = Path.Combine(imagesFolder, Path.GetFileName(character.Image ?? ""));

                    if (string.IsNullOrWhiteSpace(character.Image))
                    {
                        // Assume image already downloaded; check if file exists
                        if (!File.Exists(localImagePath))
                        {
                            missingImages.Add(character.Name);
                        }

                        // Nothing else to do
                        character.Image = localImagePath;
                        continue;
                    }

                    // Determine remote URL
                    string imageUrl = null;
                    if (string.IsNullOrEmpty(character.Image))
                    {
                        imageUrl =
                            ("https://raw.githubusercontent.com/ThePandemoniumInstitute/botc-release/refs/heads/main/resources/characters/" +
                             character.Edition + "/" + character.Name);
                        switch (character.Team)
                        {
                            case "townsfolk" or "outsider":
                                imageUrl += "_g";
                                break;
                            case "minion" or "demon":
                                imageUrl += "_e";
                                break;
                        }

                        imageUrl += ".webp";
                    }
                    else if (character.Image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        imageUrl = character.Image;
                    }
                    //*
                    else if (character.Image.StartsWith("/build", StringComparison.OrdinalIgnoreCase))
                    {
                        imageUrl = "https://www.pocketgrimoire.co.uk" + character.Image;
                    }
                    else if (character.Image.StartsWith("/img", StringComparison.OrdinalIgnoreCase))
                    {
                        imageUrl = "https://raw.githubusercontent.com/Skateside/pocket-grimoire/main/assets" +
                                   character.Image;
                    }
                    //*/
                    else
                    {
                        // Treat as local path
                        imageUrl = character.Image;
                    }

                    // Download image
                    DownloadImage(imageUrl, localImagePath);
                    character.Image = localImagePath;
                    Console.WriteLine(character.Name, character.Team, character.Image, character.FirstNight);
                }
            }
            
            // Throw error if any images are missing
            if (missingImages.Count > 0)
            {
                Console.WriteLine("Images are missing");
                foreach (var name in missingImages)
                {
                    Console.WriteLine($" - {name}");
                }
                throw new Exception("Some images are missing");
            }
            
            /*==============================================================================================================
            SECTION THAT CREATES THE PDF ITSELF
            */

            QuestPDF.Settings.License = LicenseType.Community;

            var document = QuestPDF.Fluent.Document.Create(document =>
            {
                FontManager.RegisterFont(File.OpenRead("fonts/Arlekino.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Georgia.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Georgia-Italic.ttf"));
                FontManager.RegisterFont(File.OpenRead("fonts/Candara.ttf"));
                FontManager.RegisterFontWithCustomName("TNR",File.OpenRead("fonts/Royal_Times_New_Roman.ttf"));
                document.Page(page =>
                {
                    page.DefaultTextStyle(x => x.FontFamily("TNR"));
                    page.Size(PageSizes.A4);
                    page.Margin(16);
                    
                    page.Header().Column(column =>
                    {
                        column.Item().Text($"     {scriptName}").FontSize(18).SemiBold().FontFamily("Arlekino");
                        column.Item().Text($"             by {author}").FontFamily("Georgia");
                        column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    });
                    page.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Column(contentColumn =>
                        {
                            var townImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_townsfolk.png");
                            var outsImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_outsiders.png");
                            var minImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_minions.png");
                            var demImage = Path.Combine(Directory.GetCurrentDirectory(), "data", "_demons.png");
                            contentColumn.Item().Image(townImage).FitWidth();
                            TeamDrawing("townsfolk", contentColumn, characters);
                            contentColumn.Item().Image(outsImage).FitWidth();
                            TeamDrawing("outsider", contentColumn, characters);
                            contentColumn.Item().Image(minImage).FitWidth();
                            TeamDrawing("minion", contentColumn, characters);
                            contentColumn.Item().Image(demImage).FitWidth();
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
                    page2.Margin(0);
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
                        var jinxPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "jinx.json");

                        page2.Background().Image(imagePath);
                        layers.PrimaryLayer().Row(row =>
                        {
                            row.ConstantItem(100).Column(contentColumn =>
                            {
                                string nightOrder = File.ReadAllText(NightOrderLocalPath);
                                var order = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(nightOrder);
                                
                                List<string> forder = new List<string>();
                                List<string> oorder = new List<string>();

                                if (_meta["firstNight"] is object[] array && array.Length > 0)
                                {
                                    foreach (var dat in array)
                                    {
                                        forder.Add(dat.ToString());
                                    }
                                }
                                else
                                {
                                    forder = order["firstNight"];
                                }
                                
                                foreach (string dat in forder)
                                {
                                    switch (dat)
                                    {
                                        case "dusk":
                                            NightOrderDrawing("Вечір", duskImage, null, contentColumn);
                                            break;
                                            case "minioninfo" :
                                            NightOrderDrawing("Дані міньйонів", minionImage, null, contentColumn);
                                            break;
                                        case "demoninfo":
                                            NightOrderDrawing("Дані демона", demonImage, null, contentColumn);
                                            break;
                                        case "dawn":
                                            NightOrderDrawing("Ранок", dawnImage, null, contentColumn);
                                            break;
                                        default:
                                            foreach (var character in characters)
                                            {
                                                if (character.Id == dat + "_uk")
                                                {
                                                    NightOrderDrawing(character.Name, null, character, contentColumn);
                                                }
                                            }

                                            break;
                                            
                                    }
                                    
                                }
                                
                                                                /*
                                if (_meta["otherNight"] is object[] orray && orray.Length > 0)
                                {
                                    foreach (var dat in orray)
                                    {
                                        oorder.Add(dat.ToString());
                                    }
                                }
                                else
                                {
                                    oorder = order["firstNight"];
                                }

                                foreach (string odat in oorder)
                                   {
                                       switch (odat)
                                       {
                                           case "dusk":
                                               NightOrderDrawing("Вечір", duskImage, null, contentColumn);
                                               break;
                                           case "dawn":
                                               NightOrderDrawing("Ранок", dawnImage, null, contentColumn);
                                               break;
                                           default:
                                               foreach (var character in characters)
                                               {
                                                   if (character.Id == dat + "_uk")
                                                   {
                                                       NightOrderDrawing(character.Name, null, character, contentColumn);
                                                   }
                                               }
                                               break;
                                       }*/

                            });
                            //row.RelativeItem();
                            row.ConstantItem(220).Column(contentColumn =>
                            {
                                contentColumn.Item().Height(75);
                                contentColumn.Item().Height(500).Column(jinxColumn =>
                                {
                                    if (jinxPairs.Count == 0)
                                    {
                                        jinxColumn.Item().Height(12f, Unit.Millimetre).Text("Немає жодних").FontSize(9).FontFamily("Candara").FontColor(Colors.Grey.Lighten1).AlignCenter();
                                    }
                                    else
                                    {
                                        foreach (var jinx in jinxPairs)
                                        {
                                            Console.WriteLine(String.Format($"Проклято {jinx["character1"]} та {jinx["character2"]}: {jinx["reason"]}"));
                                            var image1 = Path.Combine(Directory.GetCurrentDirectory(), "images", $"{jinx["character1"]}.webp");
                                            var image2 = Path.Combine(Directory.GetCurrentDirectory(), "images", $"{jinx["character2"]}.webp");
                                            jinxColumn.Item().Height(12f, Unit.Millimetre).Row(row =>
                                            {
                                                row.ConstantItem(14, Unit.Millimetre).AlignCenter().AlignMiddle().PaddingTop(-4).Image(image1).FitArea();
                                                row.ConstantItem(14, Unit.Millimetre).AlignCenter().AlignMiddle().PaddingTop(-4).Image(image2).FitArea();
                                                row.RelativeItem().StopPaging().Text(jinx["reason"]).FontSize(6).FontFamily("Candara");
                                            });
                                        }
                                    }


                                });

                            });

                        });
                    });
                });
            });
           
        document.GeneratePdfAndShow();
        Console.ReadLine();

        }

        private static void TeamDrawing(string team, ColumnDescriptor contentColumn, Character[] characters, bool noAbility = false)
        {
            foreach (var character in characters)
            {
                if (character.Team == team)
                {
                    contentColumn.Item().Row(row =>
                    {

                        Console.WriteLine($"{character.Name}: {character.Image}");
                        row.ConstantItem(14, Unit.Millimetre).PaddingRight(8).AlignCenter().AlignMiddle().PaddingTop(-4).Image(character.Image).FitArea();
                        if (noAbility)
                        {
                            row.RelativeItem().PaddingRight(8).Text(character.Name).SemiBold().FontSize(10);
                        }
                        else
                        {
                            row.ConstantItem(86).PaddingRight(8).Text(character.Name).SemiBold().FontSize(10);
                        }
                        //row.RelativeItem()                              {
                        if (!noAbility)
                        {
                            row.RelativeItem().Text(character.Ability).FontSize(9).FontFamily("Candara");
                        }
                        //contentColumn.Item().PaddingVertical(10);
                    });
                }
            }
        }
        
        static void SwapIdsWithData()
        {
            // Load the data from the JSON file
            string _outputFilePath;
            string _outputJson;
            string dataJson = File.ReadAllText(FullGameLocalPath);
            var data = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(dataJson);

            // Create a dictionary for quick lookup by ID
            var dataDict = new Dictionary<string, Dictionary<string, object>>();
            foreach (var entry in data)
            {
                if (entry.TryGetValue("id", out object idValue))
                {
                    string id = idValue.ToString();
                    dataDict[id] = entry;
                }
            }

            // Prompt for input JSON data or link
            Console.WriteLine("Awaiting JSON");
            string inputRaw = Console.ReadLine().Trim();

            // Detect if it's a URL
            string inputJson;
            if (Uri.TryCreate(inputRaw, UriKind.Absolute, out Uri inputUri) &&
                (inputUri.Scheme == Uri.UriSchemeHttp || inputUri.Scheme == Uri.UriSchemeHttps))
            {
                using (var client = new WebClient())
                {
                    inputJson = client.DownloadString(inputUri);
                }
            }
            else
            {
                inputJson = inputRaw;
            }

            // Optionally strip BOM / invisible characters
            inputJson = inputJson.TrimStart('\uFEFF', '\u200B', '\u0000');

            JArray inputData;
            try
            {
                inputData = JsonConvert.DeserializeObject<JArray>(inputJson);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Console.WriteLine($"Failed to parse: {ex.Message}");
                return;
            }

            // Extract the data for the output file
            _outputName = string.Empty;
            foreach (var item in inputData)
            {
                if (item is JObject jObject &&
                    jObject.TryGetValue("id", out JToken idToken) &&
                    idToken.ToString() == "_meta")
                {
                    _outputName = jObject.GetValue("name")?.ToString() ?? "";
                    _meta.Add("name",_outputName);
                    _meta.Add("author", jObject.GetValue("author")?.ToString() ?? "");
                    _meta.Add("logo", jObject.GetValue("logo")?.ToString() ?? "");
                    _meta.Add("background",jObject.GetValue("background")?.ToString() ?? "");
                    _meta.Add("almanac",jObject.GetValue("almanac")?.ToString() ?? "");
                    _meta.Add("bootlegger", jObject.GetValue("bootlegger")?.ToArray() ?? []);
                    _meta.Add("firstNight", jObject.GetValue("firstNight")?.ToArray() ?? []);
                    _meta.Add("otherNight", jObject.GetValue("otherNight")?.ToArray() ?? []);

                    foreach (var (k,v) in _meta)
                    {
                        Console.WriteLine($"{k}: {v}");
                    }

                    break;
                }
            }

            if (string.IsNullOrEmpty(_outputName))
            {
                throw new Exception("No script name detected");
            }

            // Prepare the output list
            var outputData = new List<object>();

            // Swap IDs with corresponding dictionaries
            foreach (var item in inputData)
            {
                // Case 1: string ID
                if (item.Type == JTokenType.String)
                {
                    string idString = item.ToString(); // extract string value
                    string formattedId = idString.Replace("_", "").ToLower() + "_uk";

                    if (dataDict.TryGetValue(formattedId, out var dictEntry))
                    {
                        outputData.Add(dictEntry);
                    }
                    else
                    {
                        Console.WriteLine(string.Format($"ID not found: {formattedId}"));
                        Console.WriteLine("Available IDs:");
                        foreach (var key in dataDict.Keys)
                            Console.WriteLine($" - {key}");
                        Environment.Exit(1);
                    }

                    continue;
                }

                // Case 2: JObject
                if (item.Type == JTokenType.Object)
                {
                    var jObject = (JObject)item;

                    // Preserve _meta
                    if (jObject.TryGetValue("id", out var idToken) &&
                        idToken.Type == JTokenType.String &&
                        idToken.ToString() == "_meta")
                    {
                        outputData.Add(jObject);
                        continue;
                    }

                    // Already expanded role → pass through
                    if (jObject.ContainsKey("team") || jObject.ContainsKey("ability"))
                    {
                        outputData.Add(jObject);
                        continue;
                    }

                    // JObject with id only → expand
                    if (jObject.TryGetValue("id", out idToken) &&
                        idToken.Type == JTokenType.String)
                    {
                        string formattedId = idToken.ToString().Replace("_", "").ToLower() + "_uk";
                        if (dataDict.TryGetValue(formattedId, out var dictEntry))
                        {
                            outputData.Add(dictEntry);
                        }
                        else
                        {
                            Console.WriteLine(string.Format($"ID not found: {formattedId}"));
                            Console.WriteLine("Available IDs:");
                            foreach (var key in dataDict.Keys)
                                Console.WriteLine($" - {key}");
                            Environment.Exit(1);
                        }

                        continue;
                    }

                    // Unknown JObject shape → pass through
                    outputData.Add(jObject);
                    continue;
                }

                // Case 3: everything else (numbers, arrays, etc.)
                outputData.Add(item);
            }
            _outputFilePath = $"{_workFolder}/{_outputName}.json";
            _outputJson = JsonConvert.SerializeObject(outputData, Formatting.Indented);
            File.WriteAllText(_outputFilePath, _outputJson);
            Console.WriteLine("JSON Created");
        }

        private static void UpdateData(string link, string database, string key)
        {
            // Get version tags
            string currTag = GetSetConfig(key).ToString();
            string netTag = GetFileHashTag(link);
            if (currTag != netTag)
            {
                using (var client = new WebClient())
                {
                    //Replace the file
                    Console.WriteLine($"Updating {database}");
                    client.DownloadFile(link, database);
                }
                GetSetConfig(key, false, netTag);
            }
            else
            {
                Console.WriteLine($"{database} is up to date");
            }
        }
        
        private static object? GetSetConfig(string key, bool isGet = true, object? value = null)
        {
            if (isGet)
            {
                // --- GET LOGIC ---
                if (!File.Exists(_config)) return null;

                // Read lines and look for "key="
                string? targetLine = File.ReadLines(_config)
                    .FirstOrDefault(line => line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));

                if (targetLine == null) return null;

                // Extract the raw string value after the '='
                string rawValue = targetLine.Substring(key.Length + 1);

                // Attempt to automatically parse back into native types
                if (int.TryParse(rawValue, out int intResult)) return intResult;
                if (bool.TryParse(rawValue, out bool boolResult)) return boolResult;
                
                return rawValue; // Fallback to raw string
            }
            else
            {
                // --- SET LOGIC ---
                if (value == null) return null;

                // Format how it will look in the file
                string newLine = $"{key}={value.ToString()?.Trim()}";

                if (File.Exists(_config))
                {
                    var lines = File.ReadAllLines(_config).ToList();
                    int existingIndex = lines.FindIndex(line => line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));

                    if (existingIndex != -1)
                    {
                        lines[existingIndex] = newLine; // Update line
                    }
                    else
                    {
                        lines.Add(newLine); // Append new key
                    }
                    
                    File.WriteAllLines(_config, lines);
                }
                else
                {
                    // Create file and write the very first variable
                    File.WriteAllLines(_config, new[] { newLine });
                }

                return null;
            }
        }
        
  
        private static string? GetFileHashTag(string rawFileUrl)
        {
            // Using HEAD to only fetch headers without downloading the full file
            using var request = new HttpRequestMessage(HttpMethod.Head, rawFileUrl);
        
            try
            {
                // .Result forces the async network call to run synchronously
                using HttpResponseMessage response = _client.SendAsync(request).Result;
            
                if (response.IsSuccessStatusCode && response.Headers.ETag != null)
                {
                    // This extracts the clean Git blob SHA-1 hash string
                    string gitHash = response.Headers.ETag.Tag;
                
                    Console.WriteLine($"[GitHub ETag] Current File Tag: {gitHash}");
                    return gitHash;
                }
            }
            catch (Exception ex)
            {
                // Unwrapping the AggregateException that typically occurs with .Result
                var actualMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Console.WriteLine($"[GitHub Error] Failed to fetch header: {actualMessage}");
            }
        
            return null;
        }
        public static void DownloadImage(string imageUrl, string filePath)
        {
            const int cropPercent = 25;
            using var client = new HttpClient();
            var response = client.GetAsync(imageUrl).Result;
            response.EnsureSuccessStatusCode();
            var imageBytes = response.Content.ReadAsByteArrayAsync().Result;

            using var sourceBitmap = SKBitmap.Decode(imageBytes);
            int srcWidth  = sourceBitmap.Width;
            int srcHeight = sourceBitmap.Height;

            // Replicate the original crop math exactly
            int cropHeight = (srcHeight * cropPercent) / 200;
            int top        = cropHeight - (cropHeight / 4);
            int height     = srcHeight - (cropHeight * 2);

            var srcRect  = new SKRectI(0, top, srcWidth, top + height);
            var destRect = new SKRect(0, 0, srcWidth, height);

            using var cropped = new SKBitmap(srcWidth, height);
            using var canvas  = new SKCanvas(cropped);
            canvas.DrawBitmap(sourceBitmap, srcRect, destRect);

            using var image = SKImage.FromBitmap(cropped);
            using var data  = image.Encode(SKEncodedImageFormat.Webp, 90);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }
        
        public static void NightOrderDrawing(string text, string image, Character character, ColumnDescriptor contentColumn)
        {
            Console.WriteLine(String.Format(text));
            contentColumn.Item().Height(7.5f, Unit.Millimetre).Row(row =>
            {
                if (image != null) 
                {
                    row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(image).FitArea();
                }
                else if (character != null && character.Id != "_meta")
                {
                    row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(character.Image).FitArea();
                }
                else
                {
                    row.ConstantItem(10, Unit.Millimetre).AlignCenter().PaddingTop(-4);
                }
                row.ConstantItem(64, Unit.Point).PaddingRight(8).Text(text).SemiBold().FontSize(9).AlignLeft();
                //contentColumn.Item().PaddingVertical(10);
            });
        }
        
        public static List<Dictionary<string, string>> FindJinxes()
        {
            string jinxFilePath = JinxLocalPath;
            // Load the jinx data from the JSON file
            string jinxJson = File.ReadAllText(jinxFilePath);
            string filePath = $"{_workFolder}/{_outputName}.json";
            string json = File.ReadAllText(filePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            Character[] characters = JsonSerializer.Deserialize<Character[]>(json, options);

            var jinxData = JsonConvert.DeserializeObject<List<JObject>>(jinxJson);

            // Dictionary to store jinx pairs with reasons
            var jinxPairs = new List<Dictionary<string, string>>();

            // Create a set of character IDs in the script for quick lookup
            var characterIdsInScript = new HashSet<string>(characters.Select(c => c.Id.Replace("_uk", "")));

            // Iterate through each jinx entry
            foreach (var jinxEntry in jinxData)
            {
                string characterId = jinxEntry["id"]?.ToString();

                // Check if the character exists in the script
                if (characterId != null && characterIdsInScript.Contains(characterId))
                {
                    var jinxArray = jinxEntry["jinx"] as JArray;
                    if (jinxArray != null)
                    {
                        foreach (var jinxItem in jinxArray)
                        {
                            string jinxedCharacterId = jinxItem["id"]?.ToString();
                            string reason = jinxItem["reason"]?.ToString();

                            // Check if the jinxed character also exists in the script
                            if (jinxedCharacterId != null && characterIdsInScript.Contains(jinxedCharacterId))
                            {
                                // Add the jinx pair and reason to the list
                                jinxPairs.Add(new Dictionary<string, string>
                        {
                            { "character1", characterId },
                            { "character2", jinxedCharacterId },
                            { "reason", reason }
                        });
                            }
                        }
                    }
                }
            }

            return jinxPairs;
        }
        
    }
}