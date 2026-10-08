using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HocLieu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationTokenText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "token_text",
                table: "invitations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_text",
                table: "invitations");
        }
    }
}
