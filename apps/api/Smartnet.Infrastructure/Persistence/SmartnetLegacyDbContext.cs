using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Infrastructure.Persistence;

public partial class SmartnetLegacyDbContext : DbContext
{
    public SmartnetLegacyDbContext(DbContextOptions<SmartnetLegacyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Cheque> Cheques { get; set; }

    public virtual DbSet<CnH> CnHs { get; set; }

    public virtual DbSet<CnL> CnLs { get; set; }

    public virtual DbSet<CnSeq> CnSeqs { get; set; }

    public virtual DbSet<CnSeqSt> CnSeqSts { get; set; }

    public virtual DbSet<CompaniesM> CompaniesMs { get; set; }

    public virtual DbSet<CusM> CusMs { get; set; }

    public virtual DbSet<CusSeq> CusSeqs { get; set; }

    public virtual DbSet<DelCnH> DelCnHs { get; set; }

    public virtual DbSet<DelCnL> DelCnLs { get; set; }

    public virtual DbSet<DelInvoiceH> DelInvoiceHs { get; set; }

    public virtual DbSet<DelInvoiceL> DelInvoiceLs { get; set; }

    public virtual DbSet<Docstore> Docstores { get; set; }

    public virtual DbSet<ExpCatM> ExpCatMs { get; set; }

    public virtual DbSet<ExpenseTr> ExpenseTrs { get; set; }

    public virtual DbSet<InvoiceH> InvoiceHs { get; set; }

    public virtual DbSet<InvoiceL> InvoiceLs { get; set; }

    public virtual DbSet<InvoiceLOld> InvoiceLOlds { get; set; }

    public virtual DbSet<InvoiceSeq> InvoiceSeqs { get; set; }

    public virtual DbSet<InvoiceSeqSt> InvoiceSeqSts { get; set; }

    public virtual DbSet<ItemM> ItemMs { get; set; }

    public virtual DbSet<ItemSeq> ItemSeqs { get; set; }

    public virtual DbSet<ItemStock> ItemStocks { get; set; }

    public virtual DbSet<JobsM> JobsMs { get; set; }

    public virtual DbSet<JobsSeq> JobsSeqs { get; set; }

    public virtual DbSet<JobsSeqSt> JobsSeqSts { get; set; }

    public virtual DbSet<Note> Notes { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PoH> PoHs { get; set; }

    public virtual DbSet<PoL> PoLs { get; set; }

    public virtual DbSet<PoSeq> PoSeqs { get; set; }

    public virtual DbSet<PoSeqSt> PoSeqSts { get; set; }

    public virtual DbSet<ProfitPercent> ProfitPercents { get; set; }

    public virtual DbSet<QuotationH> QuotationHs { get; set; }

    public virtual DbSet<QuotationL> QuotationLs { get; set; }

    public virtual DbSet<QuotationSeq> QuotationSeqs { get; set; }

    public virtual DbSet<QuotationSeqSt> QuotationSeqSts { get; set; }

    public virtual DbSet<SupM> SupMs { get; set; }

    public virtual DbSet<SupSeq> SupSeqs { get; set; }

    public virtual DbSet<SupplierInvPay> SupplierInvPays { get; set; }

    public virtual DbSet<SupplierInvoice> SupplierInvoices { get; set; }

    public virtual DbSet<UserM> UserMs { get; set; }

    public virtual DbSet<UserPermission> UserPermissions { get; set; }

    public virtual DbSet<VatTy> VatTies { get; set; }

    public virtual DbSet<VatValidity> VatValidities { get; set; }

    public virtual DbSet<WbProdCat> WbProdCats { get; set; }

    public virtual DbSet<WbProdSeq> WbProdSeqs { get; set; }

    public virtual DbSet<WbProduct> WbProducts { get; set; }

    public virtual DbSet<WbProject> WbProjects { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb3_unicode_ci")
            .HasCharSet("utf8mb3");

        modelBuilder.Entity<Cheque>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cheques");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Amount)
                .HasMaxLength(100)
                .HasColumnName("amount");
            entity.Property(e => e.Bank)
                .HasMaxLength(100)
                .HasColumnName("bank");
            entity.Property(e => e.Chequedate)
                .HasMaxLength(100)
                .HasColumnName("chequedate");
            entity.Property(e => e.Chkno)
                .HasMaxLength(100)
                .HasColumnName("chkno");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Createdby)
                .HasMaxLength(100)
                .HasColumnName("createdby");
            entity.Property(e => e.Createddt)
                .HasMaxLength(100)
                .HasColumnName("createddt");
            entity.Property(e => e.Duedate)
                .HasMaxLength(100)
                .HasColumnName("duedate");
            entity.Property(e => e.Entry)
                .HasMaxLength(100)
                .HasColumnName("entry");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Payto)
                .HasMaxLength(100)
                .HasColumnName("payto");
            entity.Property(e => e.Printeddt)
                .HasMaxLength(100)
                .HasColumnName("printeddt");
            entity.Property(e => e.Supcode)
                .HasMaxLength(100)
                .HasColumnName("supcode");
        });

        modelBuilder.Entity<CnH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cn_h");

            // Non-key scalars added by the Phase 5 credit-note adoption / multi-company migration — a stable
            // handle, the company scope (a credit note has no legacy `company` varchar), and the legacy/new
            // discriminator so a legacy reader can exclude the new app's rows that share this table.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.DataOrigin).HasMaxLength(16).HasColumnName("data_origin");
            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Cndate)
                .HasMaxLength(100)
                .HasColumnName("cndate");
            entity.Property(e => e.Cnno)
                .HasMaxLength(100)
                .HasColumnName("cnno");
            entity.Property(e => e.Invoiceno)
                .HasMaxLength(100)
                .HasColumnName("invoiceno");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.Stockposting)
                .HasMaxLength(100)
                .HasColumnName("stockposting");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
        });

        modelBuilder.Entity<CnL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cn_l")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Cnno)
                .HasMaxLength(100)
                .HasColumnName("cnno")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemno)
                .HasColumnType("bigint(21)")
                .HasColumnName("itemno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Tot)
                .HasMaxLength(100)
                .HasColumnName("tot")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
        });

        modelBuilder.Entity<CnSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cn_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<CnSeqSt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cn_seq_st");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<CompaniesM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("companies_m");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Vatcode)
                .HasMaxLength(100)
                .HasColumnName("vatcode");
        });

        modelBuilder.Entity<CusM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cus_m");

            entity.Property(e => e.CForm)
                .HasMaxLength(100)
                .HasColumnName("c_form");
            entity.Property(e => e.Climit)
                .HasMaxLength(100)
                .HasColumnName("climit");
            entity.Property(e => e.Contactno)
                .HasMaxLength(100)
                .HasColumnName("contactno");
            entity.Property(e => e.Contactp)
                .HasMaxLength(100)
                .HasColumnName("contactp");
            entity.Property(e => e.Cusadd)
                .HasMaxLength(100)
                .HasColumnName("cusadd");
            entity.Property(e => e.Cuscode)
                .HasMaxLength(100)
                .HasColumnName("cuscode");
            entity.Property(e => e.Cusname)
                .HasMaxLength(100)
                .HasColumnName("cusname");
            entity.Property(e => e.Custype)
                .HasMaxLength(100)
                .HasColumnName("custype");
            entity.Property(e => e.Email)
                .HasColumnType("text")
                .HasColumnName("email");
            entity.Property(e => e.Pro)
                .HasMaxLength(100)
                .HasColumnName("pro");
            entity.Property(e => e.Vatnum)
                .HasMaxLength(100)
                .HasColumnName("vatnum");
        });

        modelBuilder.Entity<CusSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("cus_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<DelCnH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("del_cn_h");

            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Cndate)
                .HasMaxLength(100)
                .HasColumnName("cndate");
            entity.Property(e => e.Cnno)
                .HasMaxLength(100)
                .HasColumnName("cnno");
            entity.Property(e => e.Deldate)
                .HasMaxLength(100)
                .HasColumnName("deldate");
            entity.Property(e => e.Delreason)
                .HasColumnType("text")
                .HasColumnName("delreason");
            entity.Property(e => e.Deluser)
                .HasMaxLength(100)
                .HasColumnName("deluser");
            entity.Property(e => e.Invoiceno)
                .HasMaxLength(100)
                .HasColumnName("invoiceno");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.Stockposting)
                .HasMaxLength(100)
                .HasColumnName("stockposting");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
        });

        modelBuilder.Entity<DelCnL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("del_cn_l")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Cnno)
                .HasMaxLength(100)
                .HasColumnName("cnno")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemno)
                .HasColumnType("bigint(21)")
                .HasColumnName("itemno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Tot)
                .HasMaxLength(100)
                .HasColumnName("tot")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
        });

        modelBuilder.Entity<DelInvoiceH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("del_invoice_h");

            entity.Property(e => e.Balance)
                .HasMaxLength(100)
                .HasColumnName("balance");
            entity.Property(e => e.Beforedisctot)
                .HasMaxLength(100)
                .HasColumnName("beforedisctot");
            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Contactperson)
                .HasMaxLength(100)
                .HasColumnName("contactperson");
            entity.Property(e => e.Cost)
                .HasMaxLength(100)
                .HasColumnName("cost");
            entity.Property(e => e.Customer)
                .HasMaxLength(100)
                .HasColumnName("customer");
            entity.Property(e => e.Deldate)
                .HasMaxLength(100)
                .HasColumnName("deldate");
            entity.Property(e => e.Delreason)
                .HasColumnType("text")
                .HasColumnName("delreason");
            entity.Property(e => e.Deluser)
                .HasMaxLength(100)
                .HasColumnName("deluser");
            entity.Property(e => e.Discountper)
                .HasMaxLength(50)
                .HasColumnName("discountper");
            entity.Property(e => e.Indate)
                .HasMaxLength(100)
                .HasColumnName("indate");
            entity.Property(e => e.Invoiceno)
                .HasMaxLength(100)
                .HasColumnName("invoiceno");
            entity.Property(e => e.Invtype)
                .HasMaxLength(100)
                .HasColumnName("invtype");
            entity.Property(e => e.It)
                .HasMaxLength(100)
                .HasColumnName("it");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Pono)
                .HasMaxLength(100)
                .HasColumnName("pono");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
        });

        modelBuilder.Entity<DelInvoiceL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("del_invoice_l");

            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc");
            entity.Property(e => e.Inno)
                .HasMaxLength(100)
                .HasColumnName("inno");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode");
            entity.Property(e => e.Itemno)
                .HasMaxLength(100)
                .HasColumnName("itemno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate");
            entity.Property(e => e.Tot)
                .HasMaxLength(100)
                .HasColumnName("tot");
        });

        modelBuilder.Entity<Docstore>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("docstore");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Addedby)
                .HasMaxLength(100)
                .HasColumnName("addedby");
            entity.Property(e => e.Addeddate)
                .HasMaxLength(100)
                .HasColumnName("addeddate");
            entity.Property(e => e.Docext)
                .HasMaxLength(100)
                .HasColumnName("docext");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Pdfdoc).HasColumnName("pdfdoc");
            entity.Property(e => e.Title)
                .HasMaxLength(100)
                .HasColumnName("title");
        });

        modelBuilder.Entity<ExpCatM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("exp_cat_m");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Expcatname)
                .HasMaxLength(100)
                .HasColumnName("expcatname");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ExpenseTr>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("expense_tr")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Addedby)
                .HasMaxLength(100)
                .HasColumnName("addedby");
            entity.Property(e => e.Addeddt)
                .HasMaxLength(100)
                .HasColumnName("addeddt");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.ExpCat)
                .HasMaxLength(100)
                .HasColumnName("exp_cat");
            entity.Property(e => e.ExpenseAmount)
                .HasMaxLength(100)
                .HasColumnName("expense_amount");
            entity.Property(e => e.ExpenseDate)
                .HasMaxLength(100)
                .HasColumnName("expense_date");
            entity.Property(e => e.ExpenseDesc)
                .HasMaxLength(100)
                .HasColumnName("expense_desc");
            entity.Property(e => e.Id)
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.PaymentRef)
                .HasMaxLength(100)
                .HasColumnName("payment_ref");
            entity.Property(e => e.Paymentm)
                .HasMaxLength(100)
                .HasColumnName("paymentm");
        });

        modelBuilder.Entity<InvoiceH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("invoice_h");

            // A non-key scalar: the surrogate id the Phase 5 adoption added, used only as a stable handle.
            entity.Property(e => e.Id).HasColumnName("id");
            // The concurrency token, so the edit screen can load a legacy invoice's row_version and send it
            // back — the edit adopts it and the version guard fires on the value the screen held.
            entity.Property(e => e.RowVersion).HasColumnName("row_version");
            entity.Property(e => e.Balance)
                .HasMaxLength(100)
                .HasColumnName("balance");
            entity.Property(e => e.Beforedisctot)
                .HasMaxLength(100)
                .HasColumnName("beforedisctot");
            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Contactperson)
                .HasMaxLength(100)
                .HasColumnName("contactperson");
            entity.Property(e => e.Cost)
                .HasMaxLength(100)
                .HasColumnName("cost");
            entity.Property(e => e.Customer)
                .HasMaxLength(100)
                .HasColumnName("customer");
            entity.Property(e => e.Discountper)
                .HasMaxLength(50)
                .HasColumnName("discountper");
            entity.Property(e => e.Indate)
                .HasMaxLength(100)
                .HasColumnName("indate");
            entity.Property(e => e.Invoiceno)
                .HasMaxLength(100)
                .HasColumnName("invoiceno");
            entity.Property(e => e.Invtype)
                .HasMaxLength(100)
                .HasColumnName("invtype");
            entity.Property(e => e.It)
                .HasMaxLength(100)
                .HasColumnName("it");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Pono)
                .HasMaxLength(100)
                .HasColumnName("pono");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
            // Added by the Phase 5 invoice adoption; lets a legacy reader exclude the new app's rows,
            // which now share this table (default 'legacy'; the new app writes 'new').
            entity.Property(e => e.DataOrigin)
                .HasMaxLength(16)
                .HasColumnName("data_origin");
        });

        modelBuilder.Entity<InvoiceL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("invoice_l")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            // The surrogate id the Phase 5 adoption added — so the edit screen can round-trip a legacy
            // line's id and the reconcile can update it in place rather than replace it.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Inno)
                .HasMaxLength(100)
                .HasColumnName("inno")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Itemno)
                .HasColumnType("bigint(21)")
                .HasColumnName("itemno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
            entity.Property(e => e.Tot)
                .HasMaxLength(100)
                .HasColumnName("tot")
                .UseCollation("utf8mb3_unicode_ci")
                .HasCharSet("utf8mb3");
        });

        modelBuilder.Entity<InvoiceLOld>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("invoice_l_old");

            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc");
            entity.Property(e => e.Inno)
                .HasMaxLength(100)
                .HasColumnName("inno");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode");
            entity.Property(e => e.Itemno)
                .HasMaxLength(100)
                .HasColumnName("itemno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate");
            entity.Property(e => e.Tot)
                .HasMaxLength(100)
                .HasColumnName("tot");
        });

        modelBuilder.Entity<InvoiceSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("invoice_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<InvoiceSeqSt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("invoice_seq_st");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ItemM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_m");

            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode");
            entity.Property(e => e.Itemname)
                .HasMaxLength(100)
                .HasColumnName("itemname");
        });

        modelBuilder.Entity<ItemSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ItemStock>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("item_stock");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Balance)
                .HasMaxLength(100)
                .HasColumnName("balance");
            entity.Property(e => e.Enteredat)
                .HasMaxLength(100)
                .HasColumnName("enteredat");
            entity.Property(e => e.Enteredby)
                .HasMaxLength(100)
                .HasColumnName("enteredby");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Indate)
                .HasMaxLength(100)
                .HasColumnName("indate");
            entity.Property(e => e.ItemCode)
                .HasMaxLength(100)
                .HasColumnName("item_code");
            entity.Property(e => e.Quantity)
                .HasMaxLength(100)
                .HasColumnName("quantity");
            entity.Property(e => e.Unitcost)
                .HasMaxLength(100)
                .HasColumnName("unitcost");
            entity.Property(e => e.Warranty)
                .HasMaxLength(100)
                .HasColumnName("warranty");
        });

        modelBuilder.Entity<JobsM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("jobs_m")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            // Non-key scalars added by the Phase 6 job-card adoption — a stable handle and the legacy/new
            // discriminator, so a legacy reader can exclude the new app's rows that share this table.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DataOrigin).HasMaxLength(16).HasColumnName("data_origin");

            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Completedby)
                .HasMaxLength(100)
                .HasColumnName("completedby");
            entity.Property(e => e.Completionremarks)
                .HasColumnType("text")
                .HasColumnName("completionremarks");
            entity.Property(e => e.Contactperson)
                .HasMaxLength(100)
                .HasColumnName("contactperson");
            entity.Property(e => e.Cost)
                .HasMaxLength(100)
                .HasColumnName("cost");
            entity.Property(e => e.Customer)
                .HasMaxLength(100)
                .HasColumnName("customer");
            entity.Property(e => e.Dompleteddt)
                .HasMaxLength(100)
                .HasColumnName("dompleteddt");
            entity.Property(e => e.Enteredby)
                .HasMaxLength(100)
                .HasColumnName("enteredby");
            entity.Property(e => e.Entereddt)
                .HasMaxLength(100)
                .HasColumnName("entereddt");
            entity.Property(e => e.FaultD)
                .HasColumnType("text")
                .HasColumnName("faultD");
            entity.Property(e => e.Items)
                .HasColumnType("text")
                .HasColumnName("items");
            entity.Property(e => e.Jdate)
                .HasMaxLength(100)
                .HasColumnName("jdate");
            entity.Property(e => e.Jobdoneby)
                .HasMaxLength(100)
                .HasColumnName("jobdoneby");
            entity.Property(e => e.Jobno)
                .HasMaxLength(100)
                .HasColumnName("jobno");
            entity.Property(e => e.Jstat)
                .HasMaxLength(100)
                .HasColumnName("jstat");
            entity.Property(e => e.Remarks)
                .HasColumnType("text")
                .HasColumnName("remarks");
            entity.Property(e => e.Sell)
                .HasMaxLength(100)
                .HasColumnName("sell");
        });

        modelBuilder.Entity<JobsSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("jobs_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<JobsSeqSt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("jobs_seq_st");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("notes");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Note1)
                .HasColumnType("text")
                .HasColumnName("note");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("payments");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Amount)
                .HasMaxLength(100)
                .HasColumnName("amount");
            entity.Property(e => e.Enteredby)
                .HasMaxLength(100)
                .HasColumnName("enteredby");
            entity.Property(e => e.Entereddt)
                .HasMaxLength(100)
                .HasColumnName("entereddt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Invoiceno)
                .HasMaxLength(100)
                .HasColumnName("invoiceno");
            entity.Property(e => e.Paym)
                .HasMaxLength(100)
                .HasColumnName("paym");
            entity.Property(e => e.Paymentrecdate)
                .HasMaxLength(100)
                .HasColumnName("paymentrecdate");
            entity.Property(e => e.Payref)
                .HasMaxLength(100)
                .HasColumnName("payref");
        });

        modelBuilder.Entity<PoH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("po_h");

            // Non-key scalars added by the Phase 6 PO adoption — a stable handle and the legacy/new
            // discriminator, so a legacy reader can exclude the new app's rows that share this table.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DataOrigin).HasMaxLength(16).HasColumnName("data_origin");

            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Nonvattotal)
                .HasMaxLength(100)
                .HasColumnName("nonvattotal");
            entity.Property(e => e.PoNo)
                .HasMaxLength(100)
                .HasColumnName("po_no");
            entity.Property(e => e.Podate)
                .HasMaxLength(100)
                .HasColumnName("podate");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.Supplier)
                .HasMaxLength(100)
                .HasColumnName("supplier");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vatpercent)
                .HasMaxLength(100)
                .HasColumnName("vatpercent");
            entity.Property(e => e.Vatty)
                .HasMaxLength(100)
                .HasColumnName("vatty");
        });

        modelBuilder.Entity<PoL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("po_l");

            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc");
            entity.Property(e => e.Itemno)
                .HasMaxLength(100)
                .HasColumnName("itemno");
            entity.Property(e => e.Pono)
                .HasMaxLength(100)
                .HasColumnName("pono");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate");
            entity.Property(e => e.Total)
                .HasMaxLength(100)
                .HasColumnName("total");
        });

        modelBuilder.Entity<PoSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("po_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<PoSeqSt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("po_seq_st");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<ProfitPercent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("profit_percent")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<QuotationH>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quotation_h");

            // Non-key scalars added by the Phase 5 quotation adoption — a stable handle and the legacy/new
            // discriminator, so a legacy reader can exclude the new app's rows that share this table.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RowVersion).HasColumnName("row_version");
            entity.Property(e => e.DataOrigin).HasMaxLength(16).HasColumnName("data_origin");
            entity.Property(e => e.ConvertedToInvoiceId).HasColumnName("converted_to_invoice_id");
            entity.Property(e => e.Beforedisctot)
                .HasMaxLength(100)
                .HasColumnName("beforedisctot");
            entity.Property(e => e.Cdatetime)
                .HasMaxLength(100)
                .HasColumnName("cdatetime");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            entity.Property(e => e.Contactperson)
                .HasMaxLength(100)
                .HasColumnName("contactperson");
            entity.Property(e => e.Customer)
                .HasMaxLength(100)
                .HasColumnName("customer");
            entity.Property(e => e.Discountper)
                .HasMaxLength(50)
                .HasColumnName("discountper");
            entity.Property(e => e.It)
                .HasMaxLength(100)
                .HasColumnName("it");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Preparedby)
                .HasMaxLength(100)
                .HasColumnName("preparedby");
            entity.Property(e => e.QNo)
                .HasMaxLength(100)
                .HasColumnName("q_no");
            entity.Property(e => e.QValid)
                .HasMaxLength(50)
                .HasColumnName("q_valid");
            entity.Property(e => e.Qdate)
                .HasMaxLength(100)
                .HasColumnName("qdate");
            entity.Property(e => e.Quotecost)
                .HasMaxLength(100)
                .HasColumnName("quotecost");
            entity.Property(e => e.Totamount)
                .HasMaxLength(100)
                .HasColumnName("totamount");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
        });

        modelBuilder.Entity<QuotationL>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quotation_l");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Desc)
                .HasColumnType("text")
                .HasColumnName("desc");
            entity.Property(e => e.Itemcode)
                .HasMaxLength(100)
                .HasColumnName("itemcode");
            entity.Property(e => e.Itemno)
                .HasMaxLength(100)
                .HasColumnName("itemno");
            entity.Property(e => e.Qno)
                .HasMaxLength(100)
                .HasColumnName("qno");
            entity.Property(e => e.Qty)
                .HasMaxLength(100)
                .HasColumnName("qty");
            entity.Property(e => e.Rate)
                .HasMaxLength(100)
                .HasColumnName("rate");
            entity.Property(e => e.Total)
                .HasMaxLength(100)
                .HasColumnName("total");
        });

        modelBuilder.Entity<QuotationSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quotation_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<QuotationSeqSt>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("quotation_seq_st");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<SupM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sup_m");

            entity.Property(e => e.Contactno)
                .HasMaxLength(100)
                .HasColumnName("contactno");
            entity.Property(e => e.Contactp)
                .HasMaxLength(100)
                .HasColumnName("contactp");
            entity.Property(e => e.Email)
                .HasColumnType("text")
                .HasColumnName("email");
            entity.Property(e => e.Supadd)
                .HasMaxLength(100)
                .HasColumnName("supadd");
            entity.Property(e => e.Supcode)
                .HasMaxLength(100)
                .HasColumnName("supcode");
            entity.Property(e => e.Supname)
                .HasMaxLength(100)
                .HasColumnName("supname");
            entity.Property(e => e.Vatnum)
                .HasMaxLength(100)
                .HasColumnName("vatnum");
        });

        modelBuilder.Entity<SupSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("sup_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<SupplierInvPay>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("supplier_inv_pay");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Paiddate)
                .HasMaxLength(100)
                .HasColumnName("paiddate");
            entity.Property(e => e.PayMethod)
                .HasMaxLength(100)
                .HasColumnName("pay_method");
            entity.Property(e => e.Referenceno)
                .HasMaxLength(100)
                .HasColumnName("referenceno");
            entity.Property(e => e.Supinvid)
                .HasMaxLength(100)
                .HasColumnName("supinvid");
        });

        modelBuilder.Entity<SupplierInvoice>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("supplier_invoice");

            entity.Property(e => e.Amount)
                .HasMaxLength(100)
                .HasColumnName("amount");
            entity.Property(e => e.Company)
                .HasMaxLength(100)
                .HasColumnName("company");
            // Promoted to a bigint primary key by the Phase 6 adoption; the legacy/new discriminator added
            // alongside so a legacy reader can exclude the new app's rows that share this table.
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DataOrigin).HasMaxLength(16).HasColumnName("data_origin");
            entity.Property(e => e.Invdate)
                .HasMaxLength(100)
                .HasColumnName("invdate");
            entity.Property(e => e.Invno)
                .HasMaxLength(100)
                .HasColumnName("invno");
            entity.Property(e => e.Novattotal)
                .HasMaxLength(100)
                .HasColumnName("novattotal");
            entity.Property(e => e.Paymentstat)
                .HasMaxLength(100)
                .HasColumnName("paymentstat");
            entity.Property(e => e.Supcode)
                .HasMaxLength(100)
                .HasColumnName("supcode");
            entity.Property(e => e.Vper)
                .HasMaxLength(100)
                .HasColumnName("vper");
            entity.Property(e => e.Vtype)
                .HasMaxLength(100)
                .HasColumnName("vtype");
        });

        modelBuilder.Entity<UserM>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("user_m")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Addedby)
                .HasMaxLength(100)
                .HasColumnName("addedby");
            entity.Property(e => e.Cuscode)
                .HasMaxLength(100)
                .HasColumnName("cuscode");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name")
                .UseCollation("utf8mb3_unicode_ci");
            entity.Property(e => e.Password)
                .HasMaxLength(100)
                .HasColumnName("password")
                .UseCollation("utf8mb3_unicode_ci");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username")
                .UseCollation("utf8mb3_unicode_ci");
            entity.Property(e => e.Ustat)
                .HasMaxLength(100)
                .HasColumnName("ustat");
            entity.Property(e => e.Utype)
                .HasMaxLength(100)
                .HasColumnName("utype")
                .UseCollation("utf8mb3_unicode_ci");
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("user_permissions")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Chequerpt)
                .HasMaxLength(100)
                .HasColumnName("chequerpt");
            entity.Property(e => e.Cheques)
                .HasMaxLength(100)
                .HasColumnName("cheques");
            entity.Property(e => e.CustomerM)
                .HasMaxLength(100)
                .HasColumnName("customer_m");
            entity.Property(e => e.CustomerOutstanding)
                .HasMaxLength(100)
                .HasColumnName("customer_outstanding");
            entity.Property(e => e.CustomersalesRpt)
                .HasMaxLength(100)
                .HasColumnName("customersales_rpt");
            entity.Property(e => e.CusvatRpt)
                .HasMaxLength(100)
                .HasColumnName("cusvat_rpt");
            entity.Property(e => e.Dashboard)
                .HasMaxLength(100)
                .HasColumnName("dashboard");
            entity.Property(e => e.DeletedIn)
                .HasMaxLength(100)
                .HasColumnName("deleted_in");
            entity.Property(e => e.Docstorage)
                .HasMaxLength(100)
                .HasColumnName("docstorage");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.Expenses)
                .HasMaxLength(100)
                .HasColumnName("expenses");
            entity.Property(e => e.ExpensesRpt)
                .HasMaxLength(100)
                .HasColumnName("expenses_rpt");
            entity.Property(e => e.ItemIn)
                .HasMaxLength(100)
                .HasColumnName("item_in");
            entity.Property(e => e.ItemM)
                .HasMaxLength(100)
                .HasColumnName("item_m");
            entity.Property(e => e.ItemQu)
                .HasMaxLength(100)
                .HasColumnName("item_qu");
            entity.Property(e => e.Itemstock)
                .HasMaxLength(100)
                .HasColumnName("itemstock");
            entity.Property(e => e.Jobcards)
                .HasMaxLength(100)
                .HasColumnName("jobcards");
            entity.Property(e => e.JobcardsRpt)
                .HasMaxLength(100)
                .HasColumnName("jobcards_rpt");
            entity.Property(e => e.NewCn)
                .HasMaxLength(100)
                .HasColumnName("new_cn");
            entity.Property(e => e.Notes)
                .HasMaxLength(100)
                .HasColumnName("notes");
            entity.Property(e => e.Payments)
                .HasMaxLength(100)
                .HasColumnName("payments");
            entity.Property(e => e.Purchaseorder)
                .HasMaxLength(100)
                .HasColumnName("purchaseorder");
            entity.Property(e => e.SalesRpt)
                .HasMaxLength(100)
                .HasColumnName("sales_rpt");
            entity.Property(e => e.SearchCn)
                .HasMaxLength(100)
                .HasColumnName("search_cn");
            entity.Property(e => e.SearchIn)
                .HasMaxLength(100)
                .HasColumnName("search_in");
            entity.Property(e => e.SearchPo)
                .HasMaxLength(100)
                .HasColumnName("search_po");
            entity.Property(e => e.SearchQu)
                .HasMaxLength(100)
                .HasColumnName("search_qu");
            entity.Property(e => e.ServiceIn)
                .HasMaxLength(100)
                .HasColumnName("service_in");
            entity.Property(e => e.ServiceQu)
                .HasMaxLength(100)
                .HasColumnName("service_qu");
            entity.Property(e => e.SupplierIn)
                .HasMaxLength(100)
                .HasColumnName("supplier_in");
            entity.Property(e => e.SupplierM)
                .HasMaxLength(100)
                .HasColumnName("supplier_m");
            entity.Property(e => e.SupplierpaymentsRpt)
                .HasMaxLength(100)
                .HasColumnName("supplierpayments_rpt");
            entity.Property(e => e.SupplierpurchaseRpt)
                .HasMaxLength(100)
                .HasColumnName("supplierpurchase_rpt");
            entity.Property(e => e.SuppliervatRpt)
                .HasMaxLength(100)
                .HasColumnName("suppliervat_rpt");
            entity.Property(e => e.UserId)
                .HasMaxLength(100)
                .HasColumnName("user_id");
            entity.Property(e => e.Users)
                .HasMaxLength(100)
                .HasColumnName("users");
        });

        modelBuilder.Entity<VatTy>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("vat_ty")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Ty)
                .HasMaxLength(100)
                .HasColumnName("ty");
        });

        modelBuilder.Entity<VatValidity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("vat_validity")
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Enddate)
                .HasMaxLength(100)
                .HasColumnName("enddate");
            entity.Property(e => e.Startdate)
                .HasMaxLength(100)
                .HasColumnName("startdate");
            entity.Property(e => e.Ty)
                .HasMaxLength(100)
                .HasColumnName("ty");
            entity.Property(e => e.Vatval)
                .HasMaxLength(100)
                .HasColumnName("vatval");
        });

        modelBuilder.Entity<WbProdCat>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wb_prod_cat");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Catname)
                .HasMaxLength(100)
                .HasColumnName("catname");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<WbProdSeq>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wb_prod_seq");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Dt)
                .HasMaxLength(100)
                .HasColumnName("dt");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
        });

        modelBuilder.Entity<WbProduct>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wb_products");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Cat)
                .HasMaxLength(100)
                .HasColumnName("cat");
            entity.Property(e => e.Descrip)
                .HasColumnType("text")
                .HasColumnName("descrip");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Imgpath)
                .HasMaxLength(100)
                .HasColumnName("imgpath");
            entity.Property(e => e.Pname)
                .HasMaxLength(100)
                .HasColumnName("pname");
            entity.Property(e => e.Price)
                .HasMaxLength(100)
                .HasColumnName("price");
        });

        modelBuilder.Entity<WbProject>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("wb_projects");

            entity.HasIndex(e => e.Id, "id");

            entity.Property(e => e.Client)
                .HasMaxLength(100)
                .HasColumnName("client");
            entity.Property(e => e.Descrip)
                .HasColumnType("text")
                .HasColumnName("descrip");
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(100)")
                .HasColumnName("id");
            entity.Property(e => e.Imagepath)
                .HasMaxLength(100)
                .HasColumnName("imagepath");
            entity.Property(e => e.Location)
                .HasMaxLength(100)
                .HasColumnName("location");
            entity.Property(e => e.Projecttitle)
                .HasMaxLength(100)
                .HasColumnName("projecttitle");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
