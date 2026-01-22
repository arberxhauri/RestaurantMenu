namespace RestaurantMenu.Helpers;

public static class FileUploadHelper
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

    public static bool IsValidImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return false;

        if (file.Length > MaxFileSize)
            return false;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    public static string GetErrorMessage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return "Please select a file.";

        if (file.Length > MaxFileSize)
            return "File size must be less than 5MB.";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return "Only .jpg, .jpeg, .png, and .gif files are allowed.";

        return string.Empty;
    }
}