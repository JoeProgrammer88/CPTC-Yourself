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

public enum CptcProgram
{
    ComputerProgramming,
    CyberPhysicalSoftwareEngineering,
    NetworkOperationsAndSystemsSecurity,
    CyberSecurity,
    Mechatronics,
    MechatronicsEngineeringTechnologyAndAutomation,
    ManufacturingEngineeringTechnologies,
    NondestructiveTesting,
    OperationsManagement
}

public static class ArtEnumExtensions
{
    public static string ToDisplayName(this CptcProgram program) => program switch
    {
        CptcProgram.ComputerProgramming                          => "Computer Programming",
        CptcProgram.CyberPhysicalSoftwareEngineering             => "Cyber-Physical Software Engineering",
        CptcProgram.NetworkOperationsAndSystemsSecurity          => "Network Operations and Systems Security",
        CptcProgram.CyberSecurity                               => "Cyber-Security",
        CptcProgram.Mechatronics                                 => "Mechatronics",
        CptcProgram.MechatronicsEngineeringTechnologyAndAutomation => "Mechatronics Engineering Technology and Automation",
        CptcProgram.ManufacturingEngineeringTechnologies         => "Manufacturing Engineering Technologies",
        CptcProgram.NondestructiveTesting                        => "Nondestructive Testing",
        CptcProgram.OperationsManagement                         => "Operations Management",
        _                                                        => program.ToString()
    };

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
