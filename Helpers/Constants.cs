namespace CarScanner.Helpers;

public static class Constants
{
    public const string DatabaseFilename = "CarScanner.db3";
    public static string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);
    public const string UnknownVehicle = "უცნობი მანქანა";
}
