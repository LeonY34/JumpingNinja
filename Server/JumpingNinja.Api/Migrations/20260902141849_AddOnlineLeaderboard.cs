using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JumpingNinja.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NinjaProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BestScore = table.Column<int>(type: "integer", nullable: false),
                    BestAchievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NinjaProfiles", x => x.Id);
                    table.CheckConstraint("CK_NinjaProfiles_BestScore_NonNegative", "\"BestScore\" >= 0");
                    table.ForeignKey(
                        name: "FK_NinjaProfiles_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountLeaderboardEntries",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BestNinjaId = table.Column<Guid>(type: "uuid", nullable: false),
                    BestScore = table.Column<int>(type: "integer", nullable: false),
                    BestAchievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLeaderboardEntries", x => x.UserId);
                    table.CheckConstraint("CK_AccountLeaderboardEntries_BestScore_NonNegative", "\"BestScore\" >= 0");
                    table.ForeignKey(
                        name: "FK_AccountLeaderboardEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountLeaderboardEntries_NinjaProfiles_BestNinjaId",
                        column: x => x.BestNinjaId,
                        principalTable: "NinjaProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegacyNinjaImports",
                columns: table => new
                {
                    LegacyProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    NinjaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyNinjaImports", x => x.LegacyProfileId);
                    table.ForeignKey(
                        name: "FK_LegacyNinjaImports_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LegacyNinjaImports_NinjaProfiles_NinjaId",
                        column: x => x.NinjaId,
                        principalTable: "NinjaProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLeaderboardEntries_BestNinjaId",
                table: "AccountLeaderboardEntries",
                column: "BestNinjaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountLeaderboardEntries_BestScore_BestAchievedAt_UserId",
                table: "AccountLeaderboardEntries",
                columns: new[] { "BestScore", "BestAchievedAt", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyNinjaImports_NinjaId",
                table: "LegacyNinjaImports",
                column: "NinjaId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyNinjaImports_OwnerUserId_NinjaId",
                table: "LegacyNinjaImports",
                columns: new[] { "OwnerUserId", "NinjaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NinjaProfiles_OwnerUserId",
                table: "NinjaProfiles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NinjaProfiles_OwnerUserId_NormalizedName",
                table: "NinjaProfiles",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLeaderboardEntries");

            migrationBuilder.DropTable(
                name: "LegacyNinjaImports");

            migrationBuilder.DropTable(
                name: "NinjaProfiles");
        }
    }
}
