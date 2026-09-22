using Forge.Shared;

namespace Forge.Plugin;

/// <summary>Host this assembly was compiled for. Set by the AutoCadYear MSBuild property.</summary>
internal static class CompiledAutoCadHost
{
#if ACAD_YEAR_2017
    public const int Year = 2017;
#elif ACAD_YEAR_2018
    public const int Year = 2018;
#elif ACAD_YEAR_2019
    public const int Year = 2019;
#elif ACAD_YEAR_2020
    public const int Year = 2020;
#elif ACAD_YEAR_2021
    public const int Year = 2021;
#elif ACAD_YEAR_2022
    public const int Year = 2022;
#elif ACAD_YEAR_2023
    public const int Year = 2023;
#elif ACAD_YEAR_2024
    public const int Year = 2024;
#elif ACAD_YEAR_2025
    public const int Year = 2025;
#elif ACAD_YEAR_2026
    public const int Year = 2026;
#else
#error AutoCadYear must be 2017-2026 so ACAD_YEAR_yyyy is defined.
#endif

    public static AutoCadHost Current => AutoCadHostCatalog.ByYear(Year);
}
