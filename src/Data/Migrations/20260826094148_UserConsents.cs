using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planara.Auth.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserConsents : Migration
    {
        // Старая версия согласий для миграции существующих пользователей
        private static readonly Guid LegacyConsentVersionId = new("e9d7178a-d998-4f99-890f-501196c3d196");

        private static readonly Guid CurrentConsentVersionId = new("f79be321-c8b4-42cf-834d-571774efad95");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    Type = table.Column<int>(
                        type: "integer",
                        nullable: false),

                    Version = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false),

                    Title = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false),

                    Content = table.Column<string>(
                        type: "text",
                        nullable: false),

                    HtmlContent = table.Column<string>(
                        type: "text",
                        nullable: false),

                    EffectiveAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    IsCurrent = table.Column<bool>(
                        type: "boolean",
                        nullable: false),

                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_ConsentVersions",
                        x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    ConsentVersionId = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    GivenAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    RevokedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true),

                    IpAddress = table.Column<string>(
                        type: "character varying(45)",
                        maxLength: 45,
                        nullable: true),

                    UserAgent = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: true),

                    RegistrationSessionId = table.Column<Guid>(
                        type: "uuid",
                        nullable: true),

                    UserCredentialId = table.Column<Guid>(
                        type: "uuid",
                        nullable: true),

                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_UserConsents",
                        x => x.Id);

                    table.ForeignKey(
                        name: "FK_UserConsents_ConsentVersions_ConsentVersionId",
                        column: x => x.ConsentVersionId,
                        principalTable: "ConsentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.ForeignKey(
                        name: "FK_UserConsents_RegistrationSessions_RegistrationSessionId",
                        column: x => x.RegistrationSessionId,
                        principalTable: "RegistrationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_UserConsents_UserCredentials_UserCredentialId",
                        column: x => x.UserCredentialId,
                        principalTable: "UserCredentials",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ConsentVersions",
                columns:
                [
                    "Id",
                    "Type",
                    "Version",
                    "Title",
                    "Content",
                    "HtmlContent",
                    "EffectiveAt",
                    "IsCurrent",
                    "CreatedAt",
                    "UpdatedAt"
                ],
                values:
                [
                    LegacyConsentVersionId,
                    0,
                    "legacy",
                    "Согласие на обработку персональных данных",
                    "",
                    "",
                    DateTime.UnixEpoch,
                    false,
                    DateTime.UtcNow,
                    DateTime.UtcNow
                ]);

            migrationBuilder.InsertData(
                table: "ConsentVersions",
                columns:
                [
                    "Id",
                    "Type",
                    "Version",
                    "Title",
                    "Content",
                    "HtmlContent",
                    "EffectiveAt",
                    "IsCurrent",
                    "CreatedAt",
                    "UpdatedAt"
                ],
                values:
                [
                    CurrentConsentVersionId,
                    0,
                    "2026-08-26",
                    "Согласие на обработку персональных данных",
                    "",
                    "",
                    new DateTime(
                        2026, 8, 26,
                        0, 0, 0,
                        DateTimeKind.Utc),
                    true,
                    DateTime.UtcNow,
                    DateTime.UtcNow
                ]);
            
            migrationBuilder.Sql(
                $"""
                INSERT INTO "UserConsents"
                (
                    "Id",
                    "ConsentVersionId",
                    "GivenAt",
                    "RevokedAt",
                    "IpAddress",
                    "UserAgent",
                    "RegistrationSessionId",
                    "UserCredentialId",
                    "CreatedAt",
                    "UpdatedAt"
                )
                SELECT
                    gen_random_uuid(),
                    '{LegacyConsentVersionId}'::uuid,
                    "ConsentGivenAt",
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    "UserId",
                    "ConsentGivenAt",
                    "ConsentGivenAt"
                FROM "UserCredentials"
                WHERE "IsConsentGiven" = TRUE;
                """);

            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                table: "UserCredentials");

            migrationBuilder.DropColumn(
                name: "IsConsentGiven",
                table: "UserCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_ConsentVersionId",
                table: "UserConsents",
                column: "ConsentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_RegistrationSessionId",
                table: "UserConsents",
                column: "RegistrationSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_UserCredentialId",
                table: "UserConsents",
                column: "UserCredentialId");
            
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_ConsentVersions_CurrentType"
                ON "ConsentVersions" ("Type")
                WHERE "IsCurrent" = TRUE;
                """);
            
            migrationBuilder.Sql(
                """
                ALTER TABLE "UserConsents"
                ADD CONSTRAINT "CK_UserConsents_Owner"
                CHECK (
                    (
                        "RegistrationSessionId" IS NOT NULL
                        AND "UserCredentialId" IS NULL
                    )
                    OR
                    (
                        "RegistrationSessionId" IS NULL
                        AND "UserCredentialId" IS NOT NULL
                    )
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                table: "UserCredentials",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: DateTime.UnixEpoch);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsentGiven",
                table: "UserCredentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            
            migrationBuilder.Sql(
                """
                UPDATE "UserCredentials" AS uc
                SET
                    "IsConsentGiven" = TRUE,
                    "ConsentGivenAt" = consent."GivenAt"
                FROM (
                    SELECT
                        uconsent."UserCredentialId",
                        MIN(uconsent."GivenAt") AS "GivenAt"
                    FROM "UserConsents" AS uconsent
                    INNER JOIN "ConsentVersions" AS version
                        ON version."Id" =
                           uconsent."ConsentVersionId"
                    WHERE
                        version."Type" = 0
                        AND uconsent."RevokedAt" IS NULL
                        AND uconsent."UserCredentialId" IS NOT NULL
                    GROUP BY
                        uconsent."UserCredentialId"
                ) AS consent
                WHERE
                    uc."UserId" =
                    consent."UserCredentialId";
                """);

            migrationBuilder.DropTable(name: "UserConsents");

            migrationBuilder.DropTable(name: "ConsentVersions");
        }
    }
}