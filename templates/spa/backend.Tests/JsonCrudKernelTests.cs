using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SpaApi.Generated;
using Xunit;

namespace SpaApi.Template.Tests;

public sealed class JsonCrudKernelTests
{
    private static readonly JsonCrudCaller DepartmentCaller = new(new HashSet<string>(StringComparer.Ordinal) { "R_department" });

    [Fact]
    public async Task Reviewed_mapping_and_scope_control_every_crud_operation()
    {
        await using var connection = await OpenCourseDatabaseAsync();
        var kernel = BuildKernel();
        var executor = new JsonCrudExecutor(kernel, connection);
        await executor.ValidateSchemaAsync();

        var first = Json("\"C1\"");
        var second = Json("\"C2\"");
        (await executor.CreateAsync(DepartmentCaller, Json("""
            {"F_name":"演算法","F_department":"D1","F_hierarchy":"H_child","F_function":"teaching","F_credits":3}
            """), first)).Should().Be(1);
        Action outsideCreate = () => executor.CreateAsync(DepartmentCaller, Json("""
            {"F_name":"外單位課程","F_department":"D2","F_hierarchy":"H_child","F_function":"teaching","F_credits":2}
            """), second).GetAwaiter().GetResult();
        outsideCreate.Should().Throw<UnauthorizedAccessException>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Courses (Id, Name, Department, Hierarchy, Function, Credits) VALUES
                ('C2', '別的單位', 'D2', 'H_child', 'teaching', 2),
                ('C3', '跨單位教務', 'D2', 'H_child', 'curriculum', 2),
                ('C4', '上層之外', 'D1', 'H_other', 'teaching', 2)
                """;
            await command.ExecuteNonQueryAsync();
        }

        var visible = await executor.QueryAsync(DepartmentCaller, Json("""{"filters":[]}"""));
        visible.Records.Select(row => row["F_id"]).Should().Equal("C1", "C3");

        var query = await executor.QueryAsync(DepartmentCaller, Json("""
            {"join":"all","filters":[{"field":"F_name","op":"contains","value":"算法"}]}
            """));
        query.Records.Should().HaveCount(1);
        query.Records[0]["F_id"].Should().Be("C1");
        query.Records[0]["F_credits"].Should().Be(3m);

        (await executor.UpdateAsync(DepartmentCaller, first, Json("""{"F_name":"演算法導論"}"""))).Should().Be(1);
        (await executor.UpdateAsync(DepartmentCaller, second, Json("""{"F_name":"不得修改"}"""))).Should().Be(0);
        (await executor.DeleteAsync(DepartmentCaller, second)).Should().Be(0);
        (await executor.DeleteAsync(DepartmentCaller, first)).Should().Be(1);
    }

    [Fact]
    public async Task Only_user_filters_are_ignored_when_interview_enabled_tolerance()
    {
        await using var connection = await OpenCourseDatabaseAsync();
        var executor = new JsonCrudExecutor(BuildKernel(ignoreInvalidUserFilters: true), connection);
        await executor.ValidateSchemaAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Courses (Id, Name, Department, Hierarchy, Function, Credits) VALUES
                ('C1', '甲', 'D1', 'H_child', 'teaching', 3),
                ('C2', '乙', 'D2', 'H_child', 'teaching', 3)
                """;
            await command.ExecuteNonQueryAsync();
        }

        var result = await executor.QueryAsync(DepartmentCaller, Json("""
            {"filters":[{"field":"F_unknown","op":"eq","value":"x"}]}
            """));
        result.IgnoredUserFilters.Should().ContainSingle().Which.Should().Be("F_unknown");
        result.Records.Should().ContainSingle();
        result.Records[0]["F_id"].Should().Be("C1");
    }

    [Fact]
    public async Task Database_reflection_rejects_wrong_mapping_and_json_cannot_write_unmapped_fields()
    {
        await using var connection = await OpenCourseDatabaseAsync();
        var wrong = BuildKernel(fields: CourseFields().Select(field => field.Code == "F_credits"
            ? field with { Type = JsonCrudType.Text }
            : field).ToArray());
        var wrongExecutor = new JsonCrudExecutor(wrong, connection);
        Func<Task> schemaCheck = () => wrongExecutor.ValidateSchemaAsync();
        await schemaCheck.Should().ThrowAsync<InvalidOperationException>().WithMessage("*F_credits*");

        var executor = new JsonCrudExecutor(BuildKernel(), connection);
        await executor.ValidateSchemaAsync();
        Action roleWrite = () => executor.CreateAsync(DepartmentCaller, Json("""
            {"F_name":"甲","F_department":"D1","F_hierarchy":"H_child","F_function":"teaching","F_credits":3,"Role":"admin"}
            """), Json("\"C1\"")).GetAwaiter().GetResult();
        roleWrite.Should().Throw<ArgumentException>();
        Action mappingWrite = () => executor.UpdateAsync(DepartmentCaller, Json("\"C1\""),
            Json("""{"F_department":"D2"}""")).GetAwaiter().GetResult();
        mappingWrite.Should().Throw<ArgumentException>();
    }

    private static JsonCrudKernel BuildKernel(bool ignoreInvalidUserFilters = false, IReadOnlyList<JsonCrudField>? fields = null)
    {
        var vertical = new JsonCrudClause("F_hierarchy", JsonCrudOperator.In, ["H_root", "H_child"]);
        var horizontal = new JsonCrudGroup(JsonCrudJoin.Any,
        [
            new JsonCrudClause("F_department", JsonCrudOperator.Equal, ["D1"]),
            new JsonCrudClause("F_function", JsonCrudOperator.Equal, ["curriculum"])
        ]);
        var scope = new JsonCrudGroup(JsonCrudJoin.All, [vertical, horizontal]);
        return new JsonCrudKernel(
            new JsonCrudResource("E_course", "Courses", "F_id", fields ?? CourseFields(), ignoreInvalidUserFilters),
            Enum.GetValues<JsonCrudAction>()
                .Select(action => new JsonCrudGrant("R_department", action, scope)).ToArray());
    }

    private static IReadOnlyList<JsonCrudField> CourseFields() =>
    [
        new("F_id", "Id", JsonCrudType.Text, true, false, false, true),
        new("F_name", "Name", JsonCrudType.Text, true, true, true, true, RequiredOnCreate: true),
        new("F_department", "Department", JsonCrudType.Text, true, true, false, true, RequiredOnCreate: true),
        new("F_hierarchy", "Hierarchy", JsonCrudType.Text, true, true, false, true, RequiredOnCreate: true),
        new("F_function", "Function", JsonCrudType.Text, true, true, false, true, RequiredOnCreate: true),
        new("F_credits", "Credits", JsonCrudType.Number, true, true, true, true, RequiredOnCreate: true)
    ];

    private static async Task<SqliteConnection> OpenCourseDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Courses (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Department TEXT NOT NULL,
                Hierarchy TEXT NOT NULL,
                Function TEXT NOT NULL,
                Credits REAL NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static JsonElement Json(string source) => JsonDocument.Parse(source).RootElement.Clone();
}
