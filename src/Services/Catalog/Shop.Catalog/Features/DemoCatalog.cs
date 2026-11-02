using Microsoft.EntityFrameworkCore;
using Shop.Catalog.Data;
using Shop.Catalog.Products;

namespace Shop.Catalog.Features;

/// <summary>A small coffee shop's catalog, for trying the stack out. Only added to an empty catalog, when asked for.</summary>
internal static class DemoCatalog
{
    private static readonly (string Category, (string Sku, string Name, string Description, decimal Price)[] Products)[] Items =
    [
        ("Kahve", [
            ("KHV-001", "Etiyopya Yirgacheffe 250 g", "Çiçeksi ve limonlu, açık kavrum. Filtre ve V60 için öğütülmemiş çekirdek.", 389.90m),
            ("KHV-002", "Kolombiya Huila 250 g", "Karamel ve kırmızı meyve notaları, orta kavrum.", 349.90m),
            ("KHV-003", "Brezilya Cerrado 1 kg", "Fındık ve çikolata, espresso için koyu kavrum.", 1149.00m),
            ("KHV-004", "Kenya AA 250 g", "Böğürtlen ve frenk üzümü, canlı asidite.", 429.00m),
            ("KHV-005", "Türk Kahvesi 500 g", "Taze çekilmiş, közde pişirmeye uygun ince öğütüm.", 289.50m),
        ]),
        ("Demleme", [
            ("DML-001", "V60 Seramik Demleme", "Tek kişilik, 02 boy seramik demleme aparatı.", 649.00m),
            ("DML-002", "Chemex 6 Fincan", "Cam gövde, ahşap boyunluk; altı fincanlık filtre kahve.", 2390.00m),
            ("DML-003", "French Press 1 L", "Paslanmaz çelik filtre, ısıya dayanıklı cam.", 899.00m),
            ("DML-004", "Kâğıt Filtre 100 Adet", "V60 02 için beyazlatılmamış filtre kâğıdı.", 129.90m),
        ]),
        ("Ekipman", [
            ("EKP-001", "Manuel Kahve Değirmeni", "Seramik çarklı, 30 kademe öğütme ayarı.", 1290.00m),
            ("EKP-002", "Hassas Terazi 0.1 g", "Zamanlayıcılı, USB şarjlı demleme terazisi.", 749.00m),
            ("EKP-003", "Kuğu Boyunlu Kettle 1 L", "Sıcaklık ayarlı, ince uçlu elektrikli su ısıtıcı.", 2690.00m),
        ]),
        ("Çay", [
            ("CAY-001", "Rize Çayı 1 kg", "Birinci hasat, demlik için siyah çay.", 319.00m),
            ("CAY-002", "Earl Grey 100 g", "Bergamot yağıyla aromalı Seylan çayı.", 189.00m),
            ("CAY-003", "Sencha Yeşil Çay 100 g", "Japon usulü buharda işlenmiş yeşil çay.", 259.00m),
            ("CAY-004", "Cam Demlik 600 ml", "Süzgeçli, ısıya dayanıklı borosilikat cam.", 449.00m),
        ]),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (await db.Categories.AnyAsync(ct))
        {
            return;
        }

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var offset = 0;
        foreach (var (categoryName, products) in Items)
        {
            var category = Category.Create(categoryName, now);
            db.Categories.Add(category);
            foreach (var (sku, name, description, price) in products)
            {
                // Spread the creation times so "newest first" has a stable, meaningful order.
                db.Products.Add(Product.Create(sku, name, description, price, category.Id, now.AddMinutes(-offset++)));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static IEnumerable<string> Skus => Items.SelectMany(i => i.Products).Select(p => p.Sku);
}
