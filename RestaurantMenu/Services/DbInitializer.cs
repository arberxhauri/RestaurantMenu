using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

public static class DbInitializer
{
    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Create roles
        // STAFF: people invited to help run someone else's branch (BranchMember).
        string[] roleNames = { "ADMIN", "OWNER", "STAFF" };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Default admin. The password never lives in code: it comes from ADMIN_INITIAL_PASSWORD,
        // or a random one is generated and written once to the server log. Either way the
        // admin must replace it on first sign-in.
        var adminEmail = "admin@restaurantmenu.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            var configured = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD");
            var password = string.IsNullOrWhiteSpace(configured) ? GeneratePassword() : configured;

            adminUser = new ApplicationUser
            {
                UserName = "admin",
                Email = adminEmail,
                FullName = "System Administrator",
                NIPT = "00000000",
                NumberOfBranches = 0,
                EmailConfirmed = true,
                MustChangePassword = true
            };

            var result = await userManager.CreateAsync(adminUser, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "ADMIN");
                if (string.IsNullOrWhiteSpace(configured))
                    Console.WriteLine($"Admin account created: {adminEmail} / {password} (change it at first sign-in)");
            }
            else
            {
                Console.WriteLine("Admin account could not be created: " +
                                  string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
        else if (await userManager.CheckPasswordAsync(adminUser, LegacyDefaultPassword))
        {
            // Older builds seeded a well-known password. Replace it so it stops working.
            var replacement = GeneratePassword();
            await userManager.RemovePasswordAsync(adminUser);
            await userManager.AddPasswordAsync(adminUser, replacement);
            adminUser.MustChangePassword = true;
            await userManager.UpdateAsync(adminUser);
            Console.WriteLine($"The admin still used the old default password. It was replaced with: {replacement} " +
                              "(change it at first sign-in)");
        }
    }

    private const string LegacyDefaultPassword = "Admin@123";

    private static string GeneratePassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string all = lower + upper + digits + "!@#$%";
        var chars = new List<char>
        {
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)]
        };
        while (chars.Count < 18) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray());
    }
}