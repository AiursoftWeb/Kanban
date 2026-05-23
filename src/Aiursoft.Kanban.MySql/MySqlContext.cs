using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
