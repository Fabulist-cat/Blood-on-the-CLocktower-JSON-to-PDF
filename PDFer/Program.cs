using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PDFer {
    public class Program
    {
        private static string outputName = string.Empty;
        public static void Main()
        {
            string dataFile = "full_game.json"; // Replace with your data file path
            SwapIdsWithData(dataFile);
            CreatePDF();
        }
        static void SwapIdsWithData(string dataFile)
        {
            // Load the data from the JSON file
            string dataJson = File.ReadAllText(dataFile);
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

            // Prompt for input JSON data
            Console.WriteLine("Paste the input JSON data: ");
            string inputJson = Console.ReadLine();
            var inputData = JsonConvert.DeserializeObject<List<object>>(inputJson);

            // Extract the name for the output file
            outputName = string.Empty;
            foreach (var item in inputData)
            {
                if (item is JObject jObject && jObject.TryGetValue("id", out JToken idToken) && idToken.ToString() == "_meta")
                {
                    outputName = jObject.GetValue("name")?.ToString();
                    break;
                }
            }

            // If outputName is not found, raise an error
            if (outputName == null)
            {
                throw new Exception("Name for output file not found in input data.");
            }

            // Prepare the output list
            var outputData = new List<object>();

            // Swap IDs with corresponding dictionaries
            foreach (var item in inputData)
            {
                if (item is string idString)
                {
                    // Remove underscores and form the ID
                    string formattedId = idString.Replace("_", "") + "_uk";
                    if (dataDict.TryGetValue(formattedId, out var dictEntry))
                    {
                        outputData.Add(dictEntry);
                    }
                    else
                    {
                        // Print an error message and stop execution if an item isn't found
                        Console.WriteLine($"Error: ID '{formattedId}' not found in data.");
                        Console.WriteLine("Available IDs:");
                        foreach (var key in dataDict.Keys)
                        {
                            Console.WriteLine($" - {key}");
                        }
                        Environment.Exit(1);
                    }
                }
                else
                {
                    outputData.Add(item);
                }
            }

            // Write the output data to a new JSON file
            string outputFilePath = $"{outputName}.json";
            string outputJson = JsonConvert.SerializeObject(outputData, Formatting.Indented);
            File.WriteAllText(outputFilePath, outputJson);
        }
        public static void CreatePDF()
        {
            string filePath = $"{outputName}.json";
            string author = string.Empty;
            string scriptName = string.Empty;
            string json = File.ReadAllText(filePath);

            // Deserialize the JSON data

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            Character[] characters = System.Text.Json.JsonSerializer.Deserialize<Character[]>(json, options);

            // Download images
            string imagesFolder = Path.Combine(Directory.GetCurrentDirectory(), "images");
            Directory.CreateDirectory(imagesFolder);

            //Find and read metadata
            foreach (var character in characters)
            {
                if (character.Id == "_meta")
                {
                    author = character.Author;
                    scriptName = character.Name;
                }
                else
                {
                    string imageUrl = character.Image.Replace("/build", "https://raw.githubusercontent.com/Skateside/pocket-grimoire/main/assets");
                    string localImagePath = Path.Combine(imagesFolder, Path.GetFileName(character.Image));
                    DownloadImage(imageUrl, localImagePath);
                    character.Image = localImagePath;
                    Console.WriteLine($"Name: {character.Name}, Team: {character.Team}, Image: {character.Image}");
                };
            }

            // Generate the PDF document
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            var document = QuestPDF.Fluent.Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.DefaultTextStyle(x => x.FontFamily(Fonts.TimesNewRoman));
                    page.Size(PageSizes.A4);
                    page.Margin(16);

                    page.Header().Column(column =>
                    {
                        column.Item().Text($"     {scriptName}").FontSize(18).SemiBold().FontFamily("Arlekino");
                        column.Item().Text($"             by {author}").FontFamily(Fonts.Georgia);
                        column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    });
                    page.Content().Layers(layers =>
                    {
                        //layers.Layer()
                        //.Background(Colors.Amber.Accent4);
                        layers.PrimaryLayer()
                        .Column(contentColumn =>
                        {
                            TeamDrawing("townsfolk", "Городяни", contentColumn, characters);
                            TeamDrawing("outsider", "Чужинці", contentColumn, characters);
                            TeamDrawing("minion", "Міньйони", contentColumn, characters);
                            TeamDrawing("demon", "Демони", contentColumn, characters);
                            if (DoesTeamExist(characters, "traveller"))
                            { TeamDrawing("traveller", "Подорожні", contentColumn, characters); }
                            if (DoesTeamExist(characters, "fabled"))
                            { TeamDrawing("fabled", "Казкові", contentColumn, characters); }
                        });
                    });
                });
            });
            //document.GeneratePdfAndShow();
            document.GeneratePdf($"D:\\ProgramOutputs\\{scriptName}.pdf");
        }

        public static void TeamDrawing(string team, string nickname, ColumnDescriptor contentColumn, Character[] characters)
        {
            contentColumn.Item().PaddingBottom(5).Text(nickname).AlignCenter().FontSize(13).SemiBold().FontFamily("Сфьикшф");
            foreach (var character in characters)
            {
                if (character.Team == team)
                {
                    contentColumn.Item().Height(7.5f, Unit.Millimetre).Row(row =>
                    {

                        Console.WriteLine($"{character.Name}: {character.Image}");
                        row.ConstantItem(64, Unit.Point).PaddingRight(8).Text(character.Name).SemiBold().FontSize(9).AlignRight();
                        row.ConstantItem(12, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(character.Image).FitArea();
                        //row.RelativeItem()                              {
                        row.RelativeItem().PaddingLeft(8).Text(character.Ability).FontSize(8);
                        //contentColumn.Item().PaddingVertical(10);
                    });
                }
            }
        }

        public static bool DoesTeamExist(Character[] characters, string teamToFind)
        {
            return characters.Any(character => character.Team == teamToFind);
        }


        public static void DownloadImage(string imageUrl, string filePath)
        {
            int cropPercent = 25;
            using (HttpClient client = new HttpClient())
            {
                var response = client.GetAsync(imageUrl).Result; // Synchronous call
                response.EnsureSuccessStatusCode();
                var imageBytes = response.Content.ReadAsByteArrayAsync().Result; // Synchronous call

                using (var imageStream = new MemoryStream(imageBytes))
                {
                    using (var image = SixLabors.ImageSharp.Image.Load(imageStream))
                    {
                        int srcWidth = image.Width;
                        int srcHeight = image.Height;

                        int cropHeight = (srcHeight * cropPercent) / 200;
                        Console.WriteLine($"{srcHeight}, {cropHeight}, {srcHeight - (cropHeight*2)}");
                        
                        var cropRectangle = new SixLabors.ImageSharp.Rectangle(0, cropHeight-(cropHeight/4), srcWidth, srcHeight - (cropHeight * 2));

                        // Crop the image
                        image.Mutate(ctx => ctx.Crop(cropRectangle));

                        // Save the cropped image
                        var imageFormat = new WebpEncoder(); // Use WebP encoder for saving
                        image.Save(filePath, imageFormat);
                    }
                }
            }
        }
    }
}
