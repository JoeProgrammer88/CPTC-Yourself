namespace CptcYourself.Client.Models;

public enum ArtStyle
{
    OilPainting,
    Cartoon,
    RealisticCartoon,
    ComicBook,
    Watercolor,
    PixelArt,
    Sketch,
    Photorealistic
}

public enum ArtGenre
{
    Humor,
    Horror,
    Caricature,
    Inspirational,
    SciFi,
    Fantasy,
    Professional
}

public static class ArtEnumExtensions
{
    public static string ToDisplayName(this ArtStyle style) => style switch
    {
        ArtStyle.OilPainting      => "Oil Painting",
        ArtStyle.Cartoon          => "Cartoon",
        ArtStyle.RealisticCartoon => "Realistic Cartoon",
        ArtStyle.ComicBook        => "Comic Book",
        ArtStyle.Watercolor       => "Watercolor",
        ArtStyle.PixelArt         => "Pixel Art",
        ArtStyle.Sketch           => "Sketch",
        ArtStyle.Photorealistic   => "Photorealistic",
        _                         => style.ToString()
    };

    public static string ToDisplayName(this ArtGenre genre) => genre switch
    {
        ArtGenre.Humor         => "Humor",
        ArtGenre.Horror        => "Horror",
        ArtGenre.Caricature    => "Caricature",
        ArtGenre.Inspirational => "Inspirational",
        ArtGenre.SciFi         => "Sci-Fi",
        ArtGenre.Fantasy       => "Fantasy",
        ArtGenre.Professional  => "Professional",
        _                      => genre.ToString()
    };
}
