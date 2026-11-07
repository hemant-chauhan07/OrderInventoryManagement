using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;

namespace OrderInventory.Infrastructure.Persistence
{
    public static class DatabaseSeeder
    {
        public static async Task SeedAsync(
            ApplicationDbContext context)
        {
            await context.Database.MigrateAsync();

            if (!await context.Users.AnyAsync())
            {
                var passwordHasher = new PasswordHasher<User>();

                var users = new List<User>
            {
                new()
                {
                    FullName = "System Administrator",
                    Email = "admin@example.com",
                    Role = UserRole.Admin
                },
                new()
                {
                    FullName = "Customer One",
                    Email = "customer1@example.com",
                    Role = UserRole.Customer
                },
                new()
                {
                    FullName = "Customer Two",
                    Email = "customer2@example.com",
                    Role = UserRole.Customer
                },
                new()
                {
                    FullName = "Customer Three",
                    Email = "customer3@example.com",
                    Role = UserRole.Customer
                }
            };

                foreach (var user in users)
                {
                    user.PasswordHash = passwordHasher.HashPassword(
                        user,
                        "ChangeMe123!");
                }

                context.Users.AddRange(users);

                await context.SaveChangesAsync();
            }

            if (!await context.Products.AnyAsync())
            {
                var products = new List<Product>
            {
                new() { Sku = "SKU001", Name = "Laptop", Price = 55000m, StockQuantity = 10 },
                new() { Sku = "SKU002", Name = "Wireless Mouse", Price = 800m, StockQuantity = 50 },
                new() { Sku = "SKU003", Name = "Keyboard", Price = 1500m, StockQuantity = 40 },
                new() { Sku = "SKU004", Name = "Monitor", Price = 12000m, StockQuantity = 15 },
                new() { Sku = "SKU005", Name = "Headphones", Price = 2500m, StockQuantity = 25 },
                new() { Sku = "SKU006", Name = "Webcam", Price = 3500m, StockQuantity = 20 },
                new() { Sku = "SKU007", Name = "USB Hub", Price = 900m, StockQuantity = 35 },
                new() { Sku = "SKU008", Name = "External SSD", Price = 6500m, StockQuantity = 12 },
                new() { Sku = "SKU009", Name = "Printer", Price = 15000m, StockQuantity = 8 },
                new() { Sku = "SKU010", Name = "Router", Price = 4000m, StockQuantity = 18 },
                new() { Sku = "SKU011", Name = "Smartphone", Price = 35000m, StockQuantity = 14 },
                new() { Sku = "SKU012", Name = "Tablet", Price = 22000m, StockQuantity = 10 },
                new() { Sku = "SKU013", Name = "Power Bank", Price = 1800m, StockQuantity = 30 },
                new() { Sku = "SKU014", Name = "Microphone", Price = 5000m, StockQuantity = 16 },
                new() { Sku = "SKU015", Name = "Desk Lamp", Price = 1200m, StockQuantity = 22 }
            };

                context.Products.AddRange(products);

                await context.SaveChangesAsync();
            }
        }
    }
}
