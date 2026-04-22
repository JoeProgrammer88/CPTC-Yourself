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
    NondestructiveTesting
}

public static class ArtEnumExtensions
{
    public static string ToDisplayName(this CptcProgram program) => program switch
    {
        CptcProgram.ComputerProgramming                            => "Computer Programming",
        CptcProgram.CyberPhysicalSoftwareEngineering               => "Cyber-Physical Software Engineering",
        CptcProgram.NetworkOperationsAndSystemsSecurity            => "Network Operations and Systems Security",
        CptcProgram.CyberSecurity                                  => "Cyber-Security",
        CptcProgram.Mechatronics                                   => "Mechatronics",
        CptcProgram.MechatronicsEngineeringTechnologyAndAutomation => "Mechatronics Engineering Technology and Automation",
        CptcProgram.ManufacturingEngineeringTechnologies           => "Manufacturing Engineering Technologies",
        CptcProgram.NondestructiveTesting                          => "Nondestructive Testing",
        _                                                          => program.ToString()
    };

    public static string ToSceneDescription(this CptcProgram program)
    {
        var options = program switch
        {
            CptcProgram.ComputerProgramming => new[]
            {
                "writing code across multiple monitors with a coffee cup nearby",
                "sketching algorithms and flowcharts on a whiteboard",
                "building a colorful interactive data dashboard",
                "pair programming with a colleague at a shared workstation",
                "debugging a stubborn issue late at night in a dimly lit office"
            },
            CptcProgram.CyberPhysicalSoftwareEngineering => new[]
            {
                "programming Arduino microcontrollers at a cluttered electronics bench",
                "soldering circuit boards under a magnifying lamp",
                "debugging embedded firmware on an oscilloscope",
                "wiring a Raspberry Pi to a custom sensor array",
                "flashing firmware onto a microcontroller and watching LEDs blink to life"
            },
            CptcProgram.NetworkOperationsAndSystemsSecurity => new[]
            {
                "monitoring live network traffic on a bank of screens in a network operations center",
                "configuring switches and routers in a humming server room",
                "responding to real-time security alerts on a glowing dashboard",
                "tracing a network intrusion across a topology map",
                "patching a firewall rule while network activity pulses across the screens"
            },
            CptcProgram.CyberSecurity => new[]
            {
                "battling cyber threats on a dark terminal with lines of scrolling code",
                "visualizing intrusion attempts on a security operations center dashboard",
                "running penetration tests in a glowing server room",
                "analyzing malware in a sandboxed virtual environment",
                "reverse engineering a suspicious binary under neon-lit monitors"
            },
            CptcProgram.Mechatronics => new[]
            {
                "operating a 6-axis industrial robot arm in a modern lab",
                "wiring servo motors and actuators on a test bench",
                "programming a humanoid robot to walk and balance",
                "calibrating sensors on an automated assembly fixture",
                "fine-tuning motor controllers while a robotic gripper picks up objects"
            },
            CptcProgram.MechatronicsEngineeringTechnologyAndAutomation => new[]
            {
                "operating a 6-axis industrial robot arm on a factory floor",
                "programming PLC automation systems at a control panel",
                "calibrating servo-driven assembly line equipment",
                "testing a conveyor system's automated sorting logic",
                "troubleshooting a pneumatic actuator on a production line"
            },
            CptcProgram.ManufacturingEngineeringTechnologies => new[]
            {
                "operating a CNC milling machine cutting a precision metal part",
                "monitoring a 3D printer building a complex metal component",
                "reading engineering blueprints at a precision workstation",
                "inspecting finished parts with calipers and measurement tools",
                "setting up tooling on a lathe in a bright machine shop"
            },
            CptcProgram.NondestructiveTesting => new[]
            {
                "using ultrasonic probes to test metal components for structural weaknesses",
                "examining X-ray images of weld joints on a light board",
                "applying dye penetrant testing to aerospace parts",
                "scanning a turbine blade with eddy current testing equipment",
                "reviewing magnetic particle inspection results on a large metal casting"
            },
            _ => new[] { "working in a high-tech professional environment with specialized equipment" }
        };

        return options[Random.Shared.Next(options.Length)];
    }

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
