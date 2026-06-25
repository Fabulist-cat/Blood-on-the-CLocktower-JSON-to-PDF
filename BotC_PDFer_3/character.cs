namespace BotC_PDFer_3;

public class Character
{
    public string Id { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;

    public string Image { get; set; } = string.Empty;
    public string Ability { get; set; } = string.Empty;
    public string Jinxes { get; set; } = string.Empty;
    public int FirstNight { get; set; } = 0;
    public int OtherNight { get; set; } = 0;
}