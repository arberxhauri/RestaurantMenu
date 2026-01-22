using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RestaurantMenu.Services
{
    public class ColorExtractionService
    {
        public async Task<ThemeColors> ExtractColorsFromImage(IFormFile imageFile)
        {
            try
            {
                using var stream = imageFile.OpenReadStream();
                using var image = await Image.LoadAsync<Rgb24>(stream);
                
                // Resize for faster processing
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(100, 100),
                    Mode = ResizeMode.Max
                }));
                
                var colorCounts = new Dictionary<string, int>();
                
                // Count pixel colors (grouped by similarity)
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image[x, y];
                        
                        // Skip very light and very dark colors
                        if (IsValidColor(pixel))
                        {
                            var hexColor = RgbToHex(pixel);
                            if (colorCounts.ContainsKey(hexColor))
                                colorCounts[hexColor]++;
                            else
                                colorCounts[hexColor] = 1;
                        }
                    }
                }
                
                // Get most common colors
                var dominantColors = colorCounts
                    .OrderByDescending(x => x.Value)
                    .Take(3)
                    .Select(x => x.Key)
                    .ToList();
                
                if (dominantColors.Count == 0)
                {
                    return GetDefaultColors();
                }
                
                return new ThemeColors
                {
                    Primary = dominantColors[0],
                    Secondary = dominantColors.Count > 1 ? dominantColors[1] : dominantColors[0],
                    Accent = dominantColors.Count > 2 ? dominantColors[2] : dominantColors[0]
                };
            }
            catch (Exception)
            {
                return GetDefaultColors();
            }
        }
        
        private bool IsValidColor(Rgb24 color)
        {
            var brightness = (color.R + color.G + color.B) / 3;
            return brightness > 30 && brightness < 225; // Exclude too light/dark
        }
        
        private string RgbToHex(Rgb24 color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        private ThemeColors GetDefaultColors()
        {
            return new ThemeColors
            {
                Primary = "#f6ad55",
                Secondary = "#ed8936",
                Accent = "#dd6b20"
            };
        }
    }
    
    public class ThemeColors
    {
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string Accent { get; set; }
    }
}
