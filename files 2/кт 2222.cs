using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace FurnitureApp;


public static class AppConfig
{
    public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string DbPath = Path.Combine(BaseDir, "database", "furniture.db");
    public static readonly string ImagesDir = Path.Combine(BaseDir, "resources", "product_images");
    public static readonly string PlaceholderImage = Path.Combine(BaseDir, "picture.png");

    public const int PhotoWidth = 300;
    public const int PhotoHeight = 200;
    public const double DiscountHighlightThreshold = 15.0;

    public static readonly Color DiscountHighlightColor = Color.FromArgb(0, 128, 128);
    public static readonly Color OutOfStockColor = Color.FromArgb(176, 176, 176);
}

public class User
{
    public int UserId { get; set; }
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoleName { get; set; } = "guest";
}

public class Product
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public int ManufacturerId { get; set; }
    public string ManufacturerName { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public double DiscountPercent { get; set; }
    public string ImagePath { get; set; } = "";

    public decimal FinalPrice => Math.Round(Price * (1 - (decimal)DiscountPercent / 100), 2);
    public bool IsPriceReduced => DiscountPercent > 0;
}

public class Order
{
    public int OrderId { get; set; }
    public string UserFullName { get; set; } = "";
    public string OrderDate { get; set; } = "";
    public string Status { get; set; } = "";
}


public sealed class DbManager
{
    private static readonly Lazy<DbManager> _instance = new(() => new DbManager());
    public static DbManager Instance => _instance.Value;

    private readonly string _connectionString;

    private const string SchemaSql = @"
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS roles (role_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS users (
    user_id INTEGER PRIMARY KEY AUTOINCREMENT,
    login TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    full_name TEXT NOT NULL,
    role_id INTEGER NOT NULL REFERENCES roles(role_id));
CREATE TABLE IF NOT EXISTS categories (category_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS manufacturers (manufacturer_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS suppliers (supplier_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS units (unit_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS products (
    product_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    description TEXT,
    category_id INTEGER NOT NULL REFERENCES categories(category_id),
    manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(manufacturer_id),
    supplier_id INTEGER NOT NULL REFERENCES suppliers(supplier_id),
    unit_id INTEGER NOT NULL REFERENCES units(unit_id),
    price REAL NOT NULL CHECK (price >= 0),
    quantity INTEGER NOT NULL CHECK (quantity >= 0),
    image_path TEXT);
CREATE TABLE IF NOT EXISTS discounts (
    discount_id INTEGER PRIMARY KEY AUTOINCREMENT,
    product_id INTEGER NOT NULL REFERENCES products(product_id) ON DELETE CASCADE,
    percent REAL NOT NULL CHECK (percent >= 0 AND percent <= 100));
CREATE TABLE IF NOT EXISTS orders (
    order_id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL REFERENCES users(user_id),
    order_date TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'новый');
CREATE TABLE IF NOT EXISTS order_items (
    order_item_id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id INTEGER NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE,
    product_id INTEGER NOT NULL REFERENCES products(product_id),
    quantity INTEGER NOT NULL CHECK (quantity > 0));
";

    private DbManager()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppConfig.DbPath)!);
        _connectionString = $"Data Source={AppConfig.DbPath};Foreign Keys=True";
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public int Execute(string sql, params (string, object?)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    public long ExecuteInsert(string sql, params (string, object?)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql + "; SELECT last_insert_rowid();";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public List<Dictionary<string, object?>> Query(string sql, params (string, object?)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var result = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }

    public Dictionary<string, object?>? QuerySingle(string sql, params (string, object?)[] parameters)
        => Query(sql, parameters).FirstOrDefault();
}

public static class Seed
{
    public static void Run()
    {
        var db = DbManager.Instance;
        var existing = db.QuerySingle("SELECT COUNT(*) AS c FROM users");
        if (existing != null && Convert.ToInt32(existing["c"]) > 0) return;

        foreach (var role in new[] { "guest", "client", "manager", "admin" })
            db.Execute("INSERT INTO roles(name) VALUES ($n)", ("$n", role));

        var users = new[]
        {
            ("client1",  AuthService.Hash("client1"),  "Иванов Иван Иванович",    2),
            ("manager1", AuthService.Hash("manager1"), "Петров Пётр Петрович",    3),
            ("admin1",   AuthService.Hash("admin1"),   "Сидоров Сидор Сидорович", 4),
        };
        foreach (var (login, hash, fio, roleId) in users)
            db.Execute("INSERT INTO users(login, password_hash, full_name, role_id) VALUES ($l, $h, $f, $r)",
                ("$l", login), ("$h", hash), ("$f", fio), ("$r", roleId));

        foreach (var n in new[] { "Столы", "Стулья", "Шкафы", "Диваны" })
            db.Execute("INSERT INTO categories(name) VALUES ($n)", ("$n", n));
        foreach (var n in new[] { "Фабрика А", "Фабрика Б", "Фабрика В" })
            db.Execute("INSERT INTO manufacturers(name) VALUES ($n)", ("$n", n));
        foreach (var n in new[] { "Поставщик 1", "Поставщик 2" })
            db.Execute("INSERT INTO suppliers(name) VALUES ($n)", ("$n", n));
        foreach (var n in new[] { "шт.", "компл." })
            db.Execute("INSERT INTO units(name) VALUES ($n)", ("$n", n));

        var products = new[]
        {
            ("Стол обеденный", "Дубовый стол",  1, 1, 1, 1, 15000.00m, 5),
            ("Стул офисный",  "Эргономичный",  2, 2, 1, 1, 4500.50m,  0),
            ("Шкаф-купе",     "Трёхдверный",   3, 1, 2, 1, 30000.00m, 2),
            ("Диван угловой", "Мягкая обивка", 4, 3, 2, 2, 55000.99m, 1),
        };
        foreach (var (name, desc, cat, man, sup, unit, price, qty) in products)
            db.Execute(@"INSERT INTO products(name, description, category_id, manufacturer_id,
                                              supplier_id, unit_id, price, quantity, image_path)
                         VALUES ($n, $d, $c, $m, $s, $u, $p, $q, '')",
                ("$n", name), ("$d", desc), ("$c", cat), ("$m", man),
                ("$s", sup), ("$u", unit), ("$p", price), ("$q", qty));

        db.Execute("INSERT INTO discounts(product_id, percent) VALUES (1, 5.0)");
        db.Execute("INSERT INTO discounts(product_id, percent) VALUES (3, 20.0)");
    }
}

public class UserRepository
{
    public Dictionary<string, object?>? FindByLogin(string login)
        => DbManager.Instance.QuerySingle(
            @"SELECT u.user_id, u.login, u.password_hash, u.full_name, r.name AS role_name
              FROM users u JOIN roles r ON r.role_id = u.role_id
              WHERE u.login = $l", ("$l", login));

    public static User ToUser(Dictionary<string, object?> row) => new()
    {
        UserId   = Convert.ToInt32(row["user_id"]),
        Login    = row["login"]?.ToString() ?? "",
        FullName = row["full_name"]?.ToString() ?? "",
        RoleName = row["role_name"]?.ToString() ?? "guest"
    };
}

public class ProductRepository
{
    private const string SelectSql = @"
        SELECT p.*, c.name AS category_name, m.name AS manufacturer_name,
               s.name AS supplier_name, u.name AS unit_name,
               COALESCE(d.percent, 0) AS discount_percent
        FROM products p
        JOIN categories c     ON c.category_id = p.category_id
        JOIN manufacturers m  ON m.manufacturer_id = p.manufacturer_id
        JOIN suppliers s      ON s.supplier_id = p.supplier_id
        JOIN units u          ON u.unit_id = p.unit_id
        LEFT JOIN discounts d ON d.product_id = p.product_id";

    public List<Product> GetAll() => DbManager.Instance.Query(SelectSql + " ORDER BY p.product_id").Select(Map).ToList();

    public Product? GetById(int id)
    {
        var rows = DbManager.Instance.Query(SelectSql + " WHERE p.product_id = $id", ("$id", id));
        return rows.Count > 0 ? Map(rows[0]) : null;
    }

    public int NextId()
    {
        var row = DbManager.Instance.QuerySingle("SELECT COALESCE(MAX(product_id), 0) + 1 AS nid FROM products");
        return Convert.ToInt32(row!["nid"]);
    }

    public int Add(Product p)
    {
        var id = (int)DbManager.Instance.ExecuteInsert(
            @"INSERT INTO products(name, description, category_id, manufacturer_id,
                                    supplier_id, unit_id, price, quantity, image_path)
              VALUES ($n, $d, $c, $m, $s, $u, $p, $q, $i)",
            ("$n", p.Name), ("$d", p.Description), ("$c", p.CategoryId),
            ("$m", p.ManufacturerId), ("$s", p.SupplierId), ("$u", p.UnitId),
            ("$p", p.Price), ("$q", p.Quantity), ("$i", p.ImagePath));
        UpsertDiscount(id, p.DiscountPercent);
        return id;
    }

    public void Update(Product p)
    {
        DbManager.Instance.Execute(
            @"UPDATE products SET name=$n, description=$d, category_id=$c,
                                   manufacturer_id=$m, supplier_id=$s, unit_id=$u,
                                   price=$p, quantity=$q, image_path=$i
              WHERE product_id=$id",
            ("$n", p.Name), ("$d", p.Description), ("$c", p.CategoryId),
            ("$m", p.ManufacturerId), ("$s", p.SupplierId), ("$u", p.UnitId),
            ("$p", p.Price), ("$q", p.Quantity), ("$i", p.ImagePath),
            ("$id", p.ProductId));
        UpsertDiscount(p.ProductId, p.DiscountPercent);
    }

    private void UpsertDiscount(int productId, double percent)
    {
        var existing = DbManager.Instance.QuerySingle("SELECT discount_id FROM discounts WHERE product_id=$id", ("$id", productId));
        if (percent > 0)
        {
            if (existing != null)
                DbManager.Instance.Execute("UPDATE discounts SET percent=$p WHERE product_id=$id", ("$p", percent), ("$id", productId));
            else
                DbManager.Instance.Execute("INSERT INTO discounts(product_id, percent) VALUES ($id, $p)", ("$id", productId), ("$p", percent));
        }
        else if (existing != null)
        {
            DbManager.Instance.Execute("DELETE FROM discounts WHERE product_id=$id", ("$id", productId));
        }
    }

    public void Delete(int productId) => DbManager.Instance.Execute("DELETE FROM products WHERE product_id=$id", ("$id", productId));

    public bool IsInOrders(int productId)
    {
        var row = DbManager.Instance.QuerySingle("SELECT COUNT(*) AS c FROM order_items WHERE product_id=$id", ("$id", productId));
        return Convert.ToInt32(row!["c"]) > 0;
    }

    public List<(int Id, string Name)> GetReference(string table, string idField)
        => DbManager.Instance.Query($"SELECT {idField} AS id, name FROM {table} ORDER BY name")
            .Select(r => (Convert.ToInt32(r["id"]), r["name"]?.ToString() ?? "")).ToList();

    private static Product Map(Dictionary<string, object?> r) => new()
    {
        ProductId        = Convert.ToInt32(r["product_id"]),
        Name             = r["name"]?.ToString() ?? "",
        Description      = r["description"]?.ToString() ?? "",
        CategoryId       = Convert.ToInt32(r["category_id"]),
        CategoryName     = r["category_name"]?.ToString() ?? "",
        ManufacturerId   = Convert.ToInt32(r["manufacturer_id"]),
        ManufacturerName = r["manufacturer_name"]?.ToString() ?? "",
        SupplierId       = Convert.ToInt32(r["supplier_id"]),
        SupplierName     = r["supplier_name"]?.ToString() ?? "",
        UnitId           = Convert.ToInt32(r["unit_id"]),
        UnitName         = r["unit_name"]?.ToString() ?? "",
        Price            = Convert.ToDecimal(r["price"]),
        Quantity         = Convert.ToInt32(r["quantity"]),
        DiscountPercent  = Convert.ToDouble(r["discount_percent"]),
        ImagePath        = r["image_path"]?.ToString() ?? ""
    };
}

public class OrderRepository
{
    public List<Order> GetAll()
        => DbManager.Instance.Query(
            @"SELECT o.order_id, u.full_name AS user_full_name, o.order_date, o.status
              FROM orders o JOIN users u ON u.user_id = o.user_id
              ORDER BY o.order_date DESC")
            .Select(r => new Order
            {
                OrderId      = Convert.ToInt32(r["order_id"]),
                UserFullName = r["user_full_name"]?.ToString() ?? "",
                OrderDate    = r["order_date"]?.ToString() ?? "",
                Status       = r["status"]?.ToString() ?? ""
            }).ToList();
}


{
    private readonly UserRepository _repo = new();

    public User? Login(string login, string password)
    {
        var row = _repo.FindByLogin(login);
        if (row == null) return null;
        if ((row["password_hash"]?.ToString() ?? "") != Hash(password)) return null;
        return UserRepository.ToUser(row);
    }

    public User LoginAsGuest() => new() { UserId = 0, Login = "guest", FullName = "Гость", RoleName = "guest" };

    public static string Hash(string password)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
}

public class SearchFilterEngine
{
    public static readonly Dictionary<string, (double Min, double Max)?> DiscountRanges = new()
    {
        ["Все диапазоны"] = null,
        ["0-10,99%"]      = (0.0, 10.99),
        ["11-14,99%"]     = (11.0, 14.99),
        ["15% и более"]   = (15.0, 100.0),
    };

    public List<Product> Apply(IEnumerable<Product> products, string query,
                               string discountRange, string? sortField, bool ascending)
    {
        var result = products.ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            result = result.Where(p => new[] { p.Name, p.Description, p.CategoryName,
                p.ManufacturerName, p.SupplierName, p.UnitName }
                .Any(f => (f ?? "").ToLowerInvariant().Contains(q))).ToList();
        }

        if (DiscountRanges.TryGetValue(discountRange, out var range) && range.HasValue)
        {
            var (min, max) = range.Value;
            result = result.Where(p => p.DiscountPercent >= min && p.DiscountPercent <= max).ToList();
        }

        if (sortField == "price")
            result = ascending ? result.OrderBy(p => p.Price).ToList() : result.OrderByDescending(p => p.Price).ToList();
        else if (sortField == "quantity")
            result = ascending ? result.OrderBy(p => p.Quantity).ToList() : result.OrderByDescending(p => p.Quantity).ToList();

        return result;
    }
}

public class ProductService
{
    private readonly ProductRepository _repo = new();

    public List<Product> GetAll() => _repo.GetAll();
    public Product? GetById(int id) => _repo.GetById(id);
    public int NextId() => _repo.NextId();
    public int Add(Product p) => _repo.Add(p);
    public void Update(Product p) => _repo.Update(p);

    public bool Delete(int productId)
    {
        if (_repo.IsInOrders(productId)) return false;
        _repo.Delete(productId);
        return true;
    }

    public List<(int Id, string Name)> GetReference(string table, string idField)
        => _repo.GetReference(table, idField);
}

public class OrderService
{
    private readonly OrderRepository _repo = new();
    public List<Order> GetAll() => _repo.GetAll();
}

public class ImageService
{
    public ImageService() => Directory.CreateDirectory(AppConfig.ImagesDir);

    public string Save(string sourcePath, string? oldPath = null)
    {
        var newPath = Path.Combine(AppConfig.ImagesDir, $"{Guid.NewGuid():N}.png");
        using var src = Image.FromFile(sourcePath);
        using var bmp = new Bitmap(AppConfig.PhotoWidth, AppConfig.PhotoHeight);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, AppConfig.PhotoWidth, AppConfig.PhotoHeight);
        }
        bmp.Save(newPath, ImageFormat.Png);
        if (!string.IsNullOrWhiteSpace(oldPath)) Delete(oldPath);
        return newPath;
    }

    public void Delete(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }

    public string GetDisplayPath(string? imagePath)
    {
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath)) return imagePath;
        return File.Exists(AppConfig.PlaceholderImage) ? AppConfig.PlaceholderImage : imagePath ?? "";
    }
}

public static class ValidationService
{
    public static (bool Ok, string Message) ValidateProductForm(
        string name, string priceText, string quantityText, string discountText)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Наименование товара не может быть пустым.");

        if (!decimal.TryParse(priceText.Replace(',', '.'), out var price))
            return (false, "Цена должна быть числом (например, 1999.99).");
        if (price < 0)
            return (false, "Цена не может быть отрицательной.");

        if (!int.TryParse(quantityText, out var quantity))
            return (false, "Количество должно быть целым числом.");
        if (quantity < 0)
            return (false, "Количество не может быть отрицательным.");

        if (!double.TryParse(discountText.Replace(',', '.'), out var discount))
            return (false, "Скидка должна быть числом.");
        if (discount < 0 || discount > 100)
            return (false, "Скидка должна быть в диапазоне 0–100%.");

        return (true, "");
    }
}

public static class NotificationService
{
    public static void ShowError(string message, string title = "Ошибка")
        => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    public static void ShowWarning(string message, string title = "Предупреждение")
        => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    public static void ShowInfo(string message, string title = "Информация")
        => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static bool Confirm(string message, string title = "Подтверждение")
        => MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}


public class LoginForm : Form
{
    private readonly AuthService _auth = new();
    private readonly TextBox _txtLogin = new();
    private readonly TextBox _txtPassword = new();

    public LoginForm()
    {
        Text = "Авторизация — Склад мебели";
        Size = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };
        layout.Controls.Add(new Label { Text = "Вход в систему", Font = new Font("Arial", 16, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "Логин", AutoSize = true });
        _txtLogin.Width = 300; layout.Controls.Add(_txtLogin);
        layout.Controls.Add(new Label { Text = "Пароль", AutoSize = true });
        _txtPassword.Width = 300; _txtPassword.PasswordChar = '*'; layout.Controls.Add(_txtPassword);

        var btnLogin = new Button { Text = "Войти", Width = 300, Height = 32 };
        btnLogin.Click += OnLogin; layout.Controls.Add(btnLogin);

        var btnGuest = new Button { Text = "Продолжить как гость", Width = 300, Height = 32 };
        btnGuest.Click += OnGuest; layout.Controls.Add(btnGuest);

        Controls.Add(layout);
    }

    private void OnLogin(object? sender, EventArgs e)
    {
        var user = _auth.Login(_txtLogin.Text, _txtPassword.Text);
        if (user == null)
        {
            NotificationService.ShowError("Неверный логин или пароль.\nПроверьте раскладку и повторите ввод.");
            return;
        }
        OpenProductList(user);
    }

    private void OnGuest(object? sender, EventArgs e) => OpenProductList(_auth.LoginAsGuest());

    private void OpenProductList(User user)
    {
        Hide();
        var form = new ProductListForm(user);
        form.FormClosed += (_, _) =>
        {
            if (form.ReturnedToLogin) Show(); else Close();
        };
        form.Show();
    }
}

public class ProductListForm : Form
{
    private readonly User _user;
    private readonly ProductService _productService = new();
    private readonly SearchFilterEngine _engine = new();
    private readonly ImageService _imageService = new();

    private readonly FlowLayoutPanel _listPanel = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true,
        FlowDirection = FlowDirection.TopDown, WrapContents = false
    };

    private readonly TextBox _txtSearch = new() { Width = 240 };
    private readonly ComboBox _cmbDiscount = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox _cmbSort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };

    private string? _sortField;
    private bool _sortAscending = true;
    public bool ReturnedToLogin { get; private set; }

    public ProductListForm(User user)
    {
        _user = user;
        Text = $"Список товаров — {RoleTitle(user.RoleName)}";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        BuildHeader();
        BuildFilters();
        Controls.Add(_listPanel);
        RefreshList();

        _txtSearch.TextChanged += (_, _) => RefreshList();
        _cmbDiscount.SelectedIndexChanged += (_, _) => RefreshList();
        _cmbSort.SelectedIndexChanged += (_, _) => ApplySortChoice();
    }

    private static string RoleTitle(string role) => role switch
    {
        "client" => "Клиент",
        "manager" => "Менеджер",
        "admin" => "Администратор",
        _ => "Гость"
    };

    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.LightGray };
        header.Controls.Add(new Label { Text = "Склад мебели", Font = new Font("Arial", 14, FontStyle.Bold), Left = 10, Top = 10, AutoSize = true });
        var lblFio = new Label { Text = $"ФИО: {_user.FullName}", AutoSize = true, Top = 12 };
        header.Controls.Add(lblFio);
        var btnLogout = new Button { Text = "Выйти", Width = 70, Height = 28, Top = 8 };
        btnLogout.Click += (_, _) => { ReturnedToLogin = true; Close(); };
        header.Controls.Add(btnLogout);

        void Layout()
        {
            btnLogout.Left = header.Width - 84;
            lblFio.Left = btnLogout.Left - lblFio.Width - 10;
        }
        header.Resize += (_, _) => Layout();
        header.HandleCreated += (_, _) => Layout();
        Controls.Add(header);
    }

    private void BuildFilters()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6) };

        if (_user.RoleName is "manager" or "admin")
        {
            panel.Controls.Add(new Label { Text = "Поиск:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
            panel.Controls.Add(_txtSearch);

            _cmbDiscount.Items.AddRange(SearchFilterEngine.DiscountRanges.Keys.Cast<object>().ToArray());
            _cmbDiscount.SelectedIndex = 0;
            panel.Controls.Add(new Label { Text = "Скидка:", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
            panel.Controls.Add(_cmbDiscount);

            _cmbSort.Items.AddRange(new object[] { "Без сортировки", "Цена ↑", "Цена ↓", "Количество ↑", "Количество ↓" });
            _cmbSort.SelectedIndex = 0;
            panel.Controls.Add(new Label { Text = "Сортировка:", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
            panel.Controls.Add(_cmbSort);
        }

        if (_user.RoleName == "admin")
        {
            var btnAdd = new Button { Text = "Добавить товар", Width = 140, Height = 30 };
            btnAdd.Click += (_, _) => OpenEdit(null);
            panel.Controls.Add(btnAdd);
        }

        Controls.Add(panel);
    }

    private void ApplySortChoice()
    {
        (_sortField, _sortAscending) = _cmbSort.SelectedIndex switch
        {
            1 => ("price", true), 2 => ("price", false),
            3 => ("quantity", true), 4 => ("quantity", false),
            _ => (null, true)
        };
        RefreshList();
    }

    private void RefreshList()
    {
        _listPanel.SuspendLayout();
        foreach (Control c in _listPanel.Controls) c.Dispose();
        _listPanel.Controls.Clear();

        var products = _productService.GetAll();
        var query = _user.RoleName is "manager" or "admin" ? _txtSearch.Text : "";
        var range = _user.RoleName is "manager" or "admin" ? _cmbDiscount.Text : "Все диапазоны";

        var filtered = _engine.Apply(products, query, range, _sortField, _sortAscending);
        foreach (var p in filtered) _listPanel.Controls.Add(BuildRow(p));

        _listPanel.ResumeLayout();
    }

    private Panel BuildRow(Product p)
    {
        Color bg = Color.White;
        if (p.Quantity == 0) bg = AppConfig.OutOfStockColor;
        else if (p.DiscountPercent > AppConfig.DiscountHighlightThreshold) bg = AppConfig.DiscountHighlightColor;
        Color fg = bg == AppConfig.DiscountHighlightColor ? Color.White : Color.Black;

        var row = new Panel
        {
            Width = Math.Max(900, _listPanel.ClientSize.Width - 25),
            Height = 110, BackColor = bg, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3)
        };

        var pic = new PictureBox { Left = 6, Top = 6, Width = 130, Height = 96, SizeMode = PictureBoxSizeMode.Zoom };
        try { pic.Image = Image.FromFile(_imageService.GetDisplayPath(p.ImagePath)); } catch { }
        row.Controls.Add(pic);

        var info = new Panel { Left = 145, Top = 4, Width = row.Width - 340, Height = 100, BackColor = bg };
        int y = 0;
        void AddLabel(string text, Font font, Color color)
        {
            var lbl = new Label { Text = text, Left = 0, Top = y, AutoSize = true, Font = font, ForeColor = color, BackColor = bg };
            info.Controls.Add(lbl);
            y += lbl.PreferredHeight;
        }
        AddLabel($"{p.CategoryName} | {p.Name}", new Font("Arial", 10, FontStyle.Bold), fg);
        AddLabel($"Описание: {p.Description}", new Font("Arial", 9), fg);
        AddLabel($"Производитель: {p.ManufacturerName}", new Font("Arial", 9), fg);
        AddLabel($"Поставщик: {p.SupplierName}", new Font("Arial", 9), fg);

        var pricePanel = new Panel { Left = 0, Top = y, Width = 400, Height = 20, BackColor = bg };
        if (p.IsPriceReduced)
        {
            pricePanel.Controls.Add(new Label { Text = p.Price.ToString("F2"), ForeColor = Color.Red,
                Font = new Font("Arial", 9, FontStyle.Strikeout), AutoSize = true, BackColor = bg });
            pricePanel.Controls.Add(new Label { Text = $"  {p.FinalPrice:F2}", ForeColor = Color.Black,
                Font = new Font("Arial", 10, FontStyle.Bold), AutoSize = true, BackColor = bg, Left = 60 });
        }
        else
        {
            pricePanel.Controls.Add(new Label { Text = p.Price.ToString("F2"), ForeColor = fg, AutoSize = true, BackColor = bg });
        }
        info.Controls.Add(pricePanel);
        y += 22;
        AddLabel($"Ед. изм.: {p.UnitName}", new Font("Arial", 9), fg);
        AddLabel($"Количество на складе: {p.Quantity}", new Font("Arial", 9), fg);
        row.Controls.Add(info);

        row.Controls.Add(new Label
        {
            Text = $"Скидка:\n{p.DiscountPercent:F2}%",
            Left = row.Width - 180, Top = 30, Width = 90, Height = 50,
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = fg, BackColor = bg
        });

        if (_user.RoleName == "admin")
        {
            var btnEdit = new Button { Text = "Ред.", Left = row.Width - 84, Top = 15, Width = 72, Height = 28 };
            var btnDel = new Button { Text = "Удал.", Left = row.Width - 84, Top = 55, Width = 72, Height = 28 };
            int pid = p.ProductId;
            btnEdit.Click += (_, _) => OpenEdit(pid);
            btnDel.Click += (_, _) => DeleteProduct(pid);
            row.Controls.Add(btnEdit);
            row.Controls.Add(btnDel);
        }

        return row;
    }

    private void OpenEdit(int? productId)
    {
        if (Application.OpenForms.OfType<ProductEditForm>().Any(f => !f.IsDisposed))
        {
            NotificationService.ShowWarning("Окно редактирования уже открыто.\nЗакройте его перед открытием нового.");
            return;
        }
        var form = new ProductEditForm(productId);
        form.Saved += () => RefreshList();
        form.Show(this);
    }

    private void DeleteProduct(int productId)
    {
        if (!NotificationService.Confirm("Вы уверены, что хотите удалить товар?")) return;
        if (!_productService.Delete(productId))
        {
            NotificationService.ShowError("Невозможно удалить товар: он присутствует в заказе.\nСначала удалите товар из всех заказов.");
            return;
        }
        NotificationService.ShowInfo("Товар удалён.");
        RefreshList();
    }
}

public class ProductEditForm : Form
{
    private readonly int? _productId;
    private readonly ProductService _productService = new();
    private readonly ImageService _imageService = new();
    public event Action? Saved;

    private readonly TextBox _txtId = new() { ReadOnly = true };
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly TextBox _txtPrice = new();
    private readonly TextBox _txtQuantity = new();
    private readonly TextBox _txtDiscount = new();
    private readonly ComboBox _cmbCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbManufacturer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbSupplier = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly PictureBox _picPhoto = new() { Width = 300, Height = 200, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

    private string? _selectedImagePath;
    private readonly List<(int Id, string Name)> _categories;
    private readonly List<(int Id, string Name)> _manufacturers;
    private readonly List<(int Id, string Name)> _suppliers;
    private readonly List<(int Id, string Name)> _units;

    public ProductEditForm(int? productId)
    {
        _productId = productId;
        Text = productId.HasValue ? "Редактирование товара" : "Добавление товара";
        Size = new Size(560, 740);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        _categories = _productService.GetReference("categories", "category_id");
        _manufacturers = _productService.GetReference("manufacturers", "manufacturer_id");
        _suppliers = _productService.GetReference("suppliers", "supplier_id");
        _units = _productService.GetReference("units", "unit_id");

        FillCombo(_cmbCategory, _categories);
        FillCombo(_cmbManufacturer, _manufacturers);
        FillCombo(_cmbSupplier, _suppliers);
        FillCombo(_cmbUnit, _units);

        BuildUi();
        if (_productId.HasValue) LoadProduct(_productId.Value);
    }

    private static void FillCombo(ComboBox combo, List<(int Id, string Name)> items)
    {
        combo.Items.Clear();
        foreach (var item in items) combo.Items.Add($"{item.Id} — {item.Name}");
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, layout.RowCount - 1);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, layout.RowCount - 1);
        }

        AddRow("ID товара", _txtId);
        AddRow("Фото (300×200)", _picPhoto);
        var btnPick = new Button { Text = "Выбрать изображение", Height = 28 };
        btnPick.Click += OnPickImage;
        AddRow("", btnPick);

        AddRow("Наименование", _txtName);
        AddRow("Категория", _cmbCategory);
        AddRow("Описание", _txtDescription);
        AddRow("Производитель", _cmbManufacturer);
        AddRow("Поставщик", _cmbSupplier);
        AddRow("Цена", _txtPrice);
        AddRow("Единица измерения", _cmbUnit);
        AddRow("Количество на складе", _txtQuantity);
        AddRow("Скидка (%)", _txtDiscount);

        var btnSave = new Button { Text = "Сохранить", Height = 32 };
        btnSave.Click += OnSave; AddRow("", btnSave);
        var btnCancel = new Button { Text = "Отмена", Height = 32 };
        btnCancel.Click += (_, _) => Close(); AddRow("", btnCancel);

        Controls.Add(layout);
    }

    private void LoadProduct(int id)
    {
        var p = _productService.GetById(id);
        if (p == null) { NotificationService.ShowError("Товар не найден."); Close(); return; }
        _txtId.Text = p.ProductId.ToString();
        _txtName.Text = p.Name;
        _txtDescription.Text = p.Description;
        _txtPrice.Text = p.Price.ToString("F2");
        _txtQuantity.Text = p.Quantity.ToString();
        _txtDiscount.Text = p.DiscountPercent.ToString("F2");
        SelectComboById(_cmbCategory, _categories, p.CategoryId);
        SelectComboById(_cmbManufacturer, _manufacturers, p.ManufacturerId);
        SelectComboById(_cmbSupplier, _suppliers, p.SupplierId);
        SelectComboById(_cmbUnit, _units, p.UnitId);
        try { _picPhoto.Image = Image.FromFile(_imageService.GetDisplayPath(p.ImagePath)); } catch { }
    }

    private static void SelectComboById(ComboBox combo, List<(int Id, string Name)> items, int targetId)
    {
        var index = items.FindIndex(i => i.Id == targetId);
        if (index >= 0) combo.SelectedIndex = index;
    }

    private void OnPickImage(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Title = "Выберите изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _selectedImagePath = dialog.FileName;
        try
        {
            using var img = Image.FromFile(dialog.FileName);
            _picPhoto.Image = new Bitmap(img, new Size(300, 200));
        }
        catch { NotificationService.ShowError("Не удалось загрузить изображение."); }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var (ok, message) = ValidationService.ValidateProductForm(
            _txtName.Text, _txtPrice.Text, _txtQuantity.Text, _txtDiscount.Text);
        if (!ok) { NotificationService.ShowError(message); return; }

        string? oldPath = _productId.HasValue ? _productService.GetById(_productId.Value)?.ImagePath : null;
        string imagePath = oldPath ?? "";
        if (!string.IsNullOrEmpty(_selectedImagePath))
            imagePath = _imageService.Save(_selectedImagePath, oldPath);

        var product = new Product
        {
            ProductId       = _productId ?? _productService.NextId(),
            Name            = _txtName.Text.Trim(),
            Description     = _txtDescription.Text.Trim(),
            CategoryId      = _categories[_cmbCategory.SelectedIndex].Id,
            ManufacturerId  = _manufacturers[_cmbManufacturer.SelectedIndex].Id,
            SupplierId      = _suppliers[_cmbSupplier.SelectedIndex].Id,
            UnitId          = _units[_cmbUnit.SelectedIndex].Id,
            Price           = decimal.Parse(_txtPrice.Text.Replace(',', '.')),
            Quantity        = int.Parse(_txtQuantity.Text),
            DiscountPercent = double.Parse(_txtDiscount.Text.Replace(',', '.')),
            ImagePath       = imagePath
        };

        if (_productId.HasValue) { _productService.Update(product); NotificationService.ShowInfo("Товар обновлён."); }
        else { _productService.Add(product); NotificationService.ShowInfo("Товар добавлен."); }

        Saved?.Invoke();
        Close();
    }
}

public class OrderForm : Form
{
    public OrderForm()
    {
        Text = "Заказы";
        Size = new Size(760, 460);
        StartPosition = FormStartPosition.CenterParent;
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true,
            AllowUserToAddRows = false, DataSource = new OrderService().GetAll()
        };
        Controls.Add(grid);
    }
}


internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Seed.Run();
        Application.Run(new LoginForm());
    }
}