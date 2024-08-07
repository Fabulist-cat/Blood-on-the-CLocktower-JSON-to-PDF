/*using PDFer;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

internal static class ProgramHelpers
{

    public static void TeamDrawing(string team, string nickname, IDocumentContainer document, Character[] characters)
    { => document.Page(page =>
    {
        page.Content().Row(contentRow =>
        {
            contentRow.AutoItem().Text("Городяни").AlignCenter().FontSize(13).SemiBold();
            page.Content().Column(contentColumn =>
            {
                foreach (var character in characters)
                {
                    contentColumn.Item().Height(8, Unit.Millimetre).Row(row =>
                    {
                        if (character.Id == "towmsfolk")
                        {
                            Console.WriteLine($"{character.Name}: {character.Image}");
                            row.ConstantItem(64, Unit.Point).PaddingRight(8).Text(character.Name).SemiBold().FontSize(9).AlignRight();
                            row.ConstantItem(12, Unit.Millimetre).AlignCenter().PaddingTop(-4).Image(character.Image).FitArea();
                            //row.RelativeItem()                              {
                            row.RelativeItem().PaddingLeft(8).Text(character.Ability).FontSize(8);
                            //contentColumn.Item().PaddingVertical(10);
                        }
                    });
                }
            });
        });
    });

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
                        Console.WriteLine($"{srcHeight}, {cropHeight}, {srcHeight - (cropHeight * 2)}");

                        var cropRectangle = new SixLabors.ImageSharp.Rectangle(0, cropHeight - (cropHeight / 4), srcWidth, srcHeight - (cropHeight * 2));

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
}*/