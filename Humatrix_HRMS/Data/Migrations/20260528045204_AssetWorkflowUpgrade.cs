using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humatrix_HRMS.Migrations
{
    /// <inheritdoc />
    public partial class AssetWorkflowUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeAssetRequests",
                columns: table => new
                {
                    EmployeeAssetRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AdditionalDetails = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedAssetCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedSpecs = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewComments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReplacementAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAssetRequests", x => x.EmployeeAssetRequestId);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "ApprovalRequestId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Assets_ReplacementAssetId",
                        column: x => x.ReplacementAssetId,
                        principalTable: "Assets",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Employees_ProcessedByEmployeeId",
                        column: x => x.ProcessedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Employees_ReviewedByEmployeeId",
                        column: x => x.ReviewedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssetRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HrProcurementRequests",
                columns: table => new
                {
                    ProcurementRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssetCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QuantityRequested = table.Column<int>(type: "int", nullable: false),
                    QuantityFulfilled = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Specifications = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreferredBrand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EstimatedBudget = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrProcurementRequests", x => x.ProcurementRequestId);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "ApprovalRequestId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_Employees_RequestedByEmployeeId",
                        column: x => x.RequestedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_Employees_ReviewedByEmployeeId",
                        column: x => x.ReviewedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrProcurementRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HrProcurementFulfillments",
                columns: table => new
                {
                    FulfillmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcurementRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FulfilledByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityFulfilled = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FulfilledAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrProcurementFulfillments", x => x.FulfillmentId);
                    table.ForeignKey(
                        name: "FK_HrProcurementFulfillments_Employees_FulfilledByEmployeeId",
                        column: x => x.FulfilledByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrProcurementFulfillments_HrProcurementRequests_ProcurementRequestId",
                        column: x => x.ProcurementRequestId,
                        principalTable: "HrProcurementRequests",
                        principalColumn: "ProcurementRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_FulfillmentId",
                table: "Assets",
                column: "FulfillmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_ApprovalRequestId",
                table: "EmployeeAssetRequests",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_AssetId",
                table: "EmployeeAssetRequests",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_EmployeeId_AssetId_Status",
                table: "EmployeeAssetRequests",
                columns: new[] { "EmployeeId", "AssetId", "Status" },
                filter: "[Status] IN ('Pending','UnderReview','Approved','InProgress')");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_EmployeeId_CreatedAt",
                table: "EmployeeAssetRequests",
                columns: new[] { "EmployeeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_OrganizationId_Status_RequestType",
                table: "EmployeeAssetRequests",
                columns: new[] { "OrganizationId", "Status", "RequestType" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_ProcessedByEmployeeId",
                table: "EmployeeAssetRequests",
                column: "ProcessedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_ReplacementAssetId",
                table: "EmployeeAssetRequests",
                column: "ReplacementAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssetRequests_ReviewedByEmployeeId",
                table: "EmployeeAssetRequests",
                column: "ReviewedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementFulfillments_FulfilledAt",
                table: "HrProcurementFulfillments",
                column: "FulfilledAt");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementFulfillments_FulfilledByEmployeeId",
                table: "HrProcurementFulfillments",
                column: "FulfilledByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementFulfillments_ProcurementRequestId",
                table: "HrProcurementFulfillments",
                column: "ProcurementRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_ApprovalRequestId",
                table: "HrProcurementRequests",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_CreatedByUserId",
                table: "HrProcurementRequests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_DepartmentId_Status",
                table: "HrProcurementRequests",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_OrganizationId_Status_RequestType",
                table: "HrProcurementRequests",
                columns: new[] { "OrganizationId", "Status", "RequestType" });

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_RequestedByEmployeeId",
                table: "HrProcurementRequests",
                column: "RequestedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_HrProcurementRequests_ReviewedByEmployeeId",
                table: "HrProcurementRequests",
                column: "ReviewedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_HrProcurementFulfillments_FulfillmentId",
                table: "Assets",
                column: "FulfillmentId",
                principalTable: "HrProcurementFulfillments",
                principalColumn: "FulfillmentId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_HrProcurementFulfillments_FulfillmentId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "EmployeeAssetRequests");

            migrationBuilder.DropTable(
                name: "HrProcurementFulfillments");

            migrationBuilder.DropTable(
                name: "HrProcurementRequests");

            migrationBuilder.DropIndex(
                name: "IX_Assets_FulfillmentId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FulfillmentId",
                table: "Assets");
        }
    }
}
