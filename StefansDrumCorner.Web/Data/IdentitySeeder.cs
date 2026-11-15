
using Microsoft.AspNetCore.Identity;

namespace StefansDrumCorner.Web.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAdminAsync(
            IServiceProvider services,
            IConfiguration configuration)
        {
            var email = configuration["AdminAccount:Email"];
            var password = configuration["AdminAccount:Password"];

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "Administrator credentials are missing from User Secrets.");
            }

            using var scope = services.CreateScope();

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<IdentityUser>>();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();

            const string adminRole = "Admin";

            // Create the Admin role if it does not exist.
            if (!await roleManager.RoleExistsAsync(adminRole))
            {
                var roleResult = await roleManager.CreateAsync(
                    new IdentityRole(adminRole));

                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }

            // Find the administrator account.
            var adminUser = await userManager.FindByEmailAsync(email);

            // Create the account if it does not exist.
            if (adminUser == null)
            {
                adminUser = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var userResult = await userManager.CreateAsync(
                    adminUser, password);

                if (!userResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        string.Join(", ", userResult.Errors.Select(e => e.Description)));
                }
            }

            // Ensure the account has the Admin role.
            if (!await userManager.IsInRoleAsync(adminUser, adminRole))
            {
                var roleResult = await userManager.AddToRoleAsync(
                    adminUser, adminRole);

                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
