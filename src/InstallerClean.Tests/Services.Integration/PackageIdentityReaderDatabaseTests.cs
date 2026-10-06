using System.Runtime.InteropServices;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The real reader against small databases built here with msi.dll: what it answers for an
/// installation package whose Property table gives no usable ProductCode, and for a file it
/// cannot read. Each database is built by the calls below, every one of which must succeed,
/// so a fixture that did not build fails the test rather than reading as a file that would
/// not open.
/// </summary>
public sealed class PackageIdentityReaderDatabaseTests : IDisposable
{
    private const string WellFormedCode = "{12345678-1234-1234-1234-123456789ABC}";

    /// <summary>The real reader, asked through the interface, as the declared-product check asks it.</summary>
    private static readonly IPackageIdentityReader Reader = new PackageIdentityReader();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public PackageIdentityReaderDatabaseTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_package_declaring_a_well_formed_ProductCode_reads_as_that_code()
    {
        // The must-hit half: the same building, with a code the reader accepts. Without it,
        // the tests below could pass over databases the building never made.
        var path = APackage(("ProductCode", WellFormedCode), ("ProductName", "Something"));

        var identity = Reader.Read(path, isPatch: false, out var detail, out _);

        Assert.NotNull(identity);
        Assert.Equal(WellFormedCode, identity.Value.Code);
        Assert.False(identity.Value.IsPatch);
        Assert.Equal(string.Empty, detail);
    }

    [Fact]
    public void A_package_with_no_ProductCode_row_declares_no_code()
    {
        var path = APackage(("ProductName", "Something"));

        var identity = Reader.Read(path, isPatch: false, out var detail, out var refusal);

        Assert.Null(identity);
        Assert.Equal(PackageReadRefusal.DeclaresNoCode, refusal);
        Assert.Equal("package declares no ProductCode", detail);
    }

    [Fact]
    public void A_package_whose_ProductCode_is_not_a_GUID_declares_no_code()
    {
        var path = APackage(("ProductCode", "not-a-guid"), ("ProductName", "Something"));

        var identity = Reader.Read(path, isPatch: false, out var detail, out var refusal);

        Assert.Null(identity);
        Assert.Equal(PackageReadRefusal.DeclaresNoCode, refusal);
        Assert.Equal("ProductCode is not a well-formed GUID", detail);
    }

    [Fact]
    public void A_database_with_no_Property_table_would_not_read()
    {
        // The query naming the table does not open, which the reader cannot tell from a query
        // this build of msi.dll would not prepare, so it claims nothing about what the file
        // declares.
        var path = ADatabase(
            "CREATE TABLE `Other` (`Key` CHAR(72) NOT NULL PRIMARY KEY `Key`)",
            "INSERT INTO `Other` (`Key`) VALUES ('Something')");

        var identity = Reader.Read(path, isPatch: false, out var detail, out var refusal);

        Assert.Null(identity);
        Assert.Equal(PackageReadRefusal.WouldNotRead, refusal);
        Assert.StartsWith("Property table query would not open", detail);
    }

    [Fact]
    public void A_file_that_is_not_a_database_would_not_read()
    {
        var path = Path.Combine(_folder, "text.msi");
        File.WriteAllText(path, "not a database");

        var identity = Reader.Read(path, isPatch: false, out var detail, out var refusal);

        Assert.Null(identity);
        Assert.Equal(PackageReadRefusal.WouldNotRead, refusal);
        Assert.StartsWith("package would not open as a database", detail);
    }

    [Fact]
    public void The_plain_read_answers_as_the_read_that_says_why()
    {
        var path = APackage(("ProductCode", "not-a-guid"));
        var reader = Reader;

        var plain = reader.Read(path, isPatch: false, out var plainDetail);
        var withRefusal = reader.Read(path, isPatch: false, out var detail, out _);

        Assert.Equal(withRefusal, plain);
        Assert.Equal(detail, plainDetail);
    }

    /// <summary>A database whose Property table holds the rows given.</summary>
    private string APackage(params (string Property, string Value)[] rows) =>
        ADatabase(
        [
            "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)",
            .. rows.Select(row => $"INSERT INTO `Property` (`Property`, `Value`) VALUES ('{row.Property}', '{row.Value}')"),
        ]);

    /// <summary>A new database in the test's folder, built by running each query and committed.</summary>
    private string ADatabase(params string[] queries)
    {
        var path = Path.Combine(_folder, Guid.NewGuid() + ".msi");
        Assert.Equal(0u, MsiOpenDatabaseW(path, CreateMode, out var database));
        try
        {
            foreach (var query in queries)
            {
                Assert.Equal(0u, MsiDatabaseOpenViewW(database, query, out var view));
                try
                {
                    Assert.Equal(0u, MsiViewExecute(view, 0));
                }
                finally
                {
                    MsiCloseHandle(view);
                }
            }

            Assert.Equal(0u, MsiDatabaseCommit(database));
        }
        finally
        {
            MsiCloseHandle(database);
        }

        return path;
    }

    /// <summary>MSIDBOPEN_CREATE in msiquery.h, passed as the pointer value it is declared as.</summary>
    private static readonly IntPtr CreateMode = 3;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabaseW(string szDatabasePath, IntPtr szPersist, out uint phDatabase);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenViewW(uint hDatabase, string szQuery, out uint phView);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(uint hView, uint hRecord);

    [DllImport("msi.dll")]
    private static extern uint MsiDatabaseCommit(uint hDatabase);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint hAny);
}
