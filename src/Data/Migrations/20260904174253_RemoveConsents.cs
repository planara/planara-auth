using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planara.Auth.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
            """
                WITH consents AS
                (
                    SELECT
                        gen_random_uuid() AS "RequestId",
                        uc."ConsentVersionId",
                        uc."GivenAt",
                        uc."IpAddress",
                        uc."UserAgent",
                        uc."UserCredentialId",
                        cv."Type"
                    FROM "UserConsents" uc
                    INNER JOIN "ConsentVersions" cv
                        ON cv."Id" = uc."ConsentVersionId"
                    WHERE
                        uc."RevokedAt" IS NULL
                        AND uc."UserCredentialId" IS NOT NULL
                )
                INSERT INTO "OutboxMessages"
                (
                    "Id",
                    "TopicKey",
                    "Type",
                    "Key",
                    "PayloadJson",
                    "CreatedAt",
                    "UpdatedAt"
                )
                SELECT
                    gen_random_uuid(),
                    'ConsentGrantRequested',
                    'ConsentGrantRequestedMessage',
                    c."UserCredentialId"::text,
                    json_build_object(
                        'RequestId', c."RequestId",
                        'RegistrationId', NULL,
                        'UserId', c."UserCredentialId",
                        'Type', c."Type",
                        'ConsentVersionId', c."ConsentVersionId",
                        'GivenAt', c."GivenAt",
                        'ExpiresAt', NULL,
                        'IpAddress', c."IpAddress",
                        'UserAgent', c."UserAgent"
                    )::text,
                    NOW(),
                    NOW()
                FROM consents c;
            """);
            
            migrationBuilder.DropTable(
                name: "UserConsents");

            migrationBuilder.DropTable(
                name: "ConsentVersions");

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailConfirmed",
                table: "UserCredentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserConsentProjections",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ConsentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsentProjections", x => new { x.UserId, x.Type });
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserConsentProjections_ConsentVersionId",
                table: "UserConsentProjections",
                column: "ConsentVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserConsentProjections");

            migrationBuilder.DropColumn(
                name: "IsEmailConfirmed",
                table: "UserCredentials");

            migrationBuilder.CreateTable(
                name: "ConsentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HtmlContent = table.Column<string>(type: "text", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GivenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsents", x => x.Id);
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
        }
    }
}
