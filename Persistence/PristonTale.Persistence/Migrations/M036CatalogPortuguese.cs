using FluentMigrator;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Persistence.Migrations;

// Brazilian Portuguese joins the catalog languages, so its *_texts and npc_dialog_lines rows can be stored. A database
// made while migration 28 seeded it already has the row.
[Migration(36)]
public sealed class M036CatalogPortuguese : Migration
{
    public override void Up() => Execute.Sql(
        $"INSERT INTO languages (code, name) SELECT '{CatalogLanguages.BrazilianPortuguese}', 'Portuguese (Brazil)' "
        + $"WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = '{CatalogLanguages.BrazilianPortuguese}')");

    public override void Down() =>
        Delete.FromTable("languages").Row(new { code = CatalogLanguages.BrazilianPortuguese });
}
