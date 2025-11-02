using AppHospedagemAPI.Models;
using AppHospedagemAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using AppHospedagemAPI.DTOs;

namespace AppHospedagemAPI.Endpoints
{
    public static class QuartoEndpoints
    {
        public static void MapQuartoEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/quartos")
                .WithTags("Quartos")
                .RequireAuthorization(); // Todos exigem autenticação

            // 📋 LISTAR QUARTOS
            group.MapGet("/", async (
                [FromQuery] string? grupo,
                [FromQuery] int? capacidadeMinima,
                [FromQuery] bool? disponivel,
                AppDbContext db) =>
            {
                var query = db.Quartos.AsQueryable();

                if (!string.IsNullOrEmpty(grupo))
                    query = query.Where(q => q.Grupo == grupo);

                if (capacidadeMinima.HasValue)
                    query = query.Where(q => q.QuantidadeCamas >= capacidadeMinima.Value);

                if (disponivel.HasValue)
                {
                    query = query.Include(q => q.Locacoes);

                    query = disponivel.Value
                        ? query.Where(q => !q.EstaOcupado)
                        : query.Where(q => q.EstaOcupado);
                }

                var quartos = await query.OrderBy(q => q.Numero).ToListAsync();

                return Results.Ok(quartos.Select(q => new QuartoResponse
                {
                    Id = q.Id,
                    Numero = q.Numero,
                    QuantidadeCamas = q.QuantidadeCamas,
                    Grupo = q.Grupo,
                    EstaOcupado = q.EstaOcupado
                }));
            })
            .WithSummary("Listar quartos com filtros")
            .Produces<IEnumerable<QuartoResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🔍 OBTER DETALHES DE UM QUARTO
            group.MapGet("/{id}", async (int id, AppDbContext db) =>
            {
                var quarto = await db.Quartos
                    .Include(q => q.Locacoes)
                    .FirstOrDefaultAsync(q => q.Id == id);

                if (quarto == null)
                    return Results.NotFound("Quarto não encontrado.");

                return Results.Ok(new QuartoResponse
                {
                    Id = quarto.Id,
                    Numero = quarto.Numero,
                    QuantidadeCamas = quarto.QuantidadeCamas,
                    Grupo = quarto.Grupo,
                    EstaOcupado = quarto.EstaOcupado
                });
            })
            .WithSummary("Obtém detalhes de um quarto")
            .Produces<QuartoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // ➕ CADASTRAR NOVO QUARTO (apenas admin)
            group.MapPost("/", async (
                [FromBody] QuartoCreateRequest request,
                AppDbContext db) =>
            {
                if (await db.Quartos.AnyAsync(q => q.Numero == request.Numero))
                    return Results.BadRequest("Já existe um quarto com este número.");

                var quarto = new Quarto
                {
                    Numero = request.Numero,
                    QuantidadeCamas = request.QuantidadeCamas,
                    Grupo = request.Grupo
                };

                db.Quartos.Add(quarto);
                await db.SaveChangesAsync();

                return Results.Created($"/quartos/{quarto.Id}", new QuartoResponse
                {
                    Id = quarto.Id,
                    Numero = quarto.Numero,
                    QuantidadeCamas = quarto.QuantidadeCamas,
                    Grupo = quarto.Grupo,
                    EstaOcupado = quarto.EstaOcupado // apenas leitura
                });
            })
            .RequireAuthorization("admin")
            .WithSummary("Cadastra um novo quarto")
            .Produces<QuartoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);


            // ✏️ ATUALIZAR QUARTO (apenas admin)
            group.MapPut("/{id}", async (
                int id,
                [FromBody] QuartoUpdateRequest request,
                AppDbContext db) =>
            {
                var quarto = await db.Quartos.FindAsync(id);
                if (quarto == null)
                    return Results.NotFound("Quarto não encontrado para atualização.");

                if (await db.Quartos.AnyAsync(q => q.Numero == request.Numero && q.Id != id))
                    return Results.BadRequest("Já existe outro quarto com este número.");

                quarto.Numero = request.Numero;
                quarto.QuantidadeCamas = request.QuantidadeCamas;
                quarto.Grupo = request.Grupo;

                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .RequireAuthorization("admin")
            .WithSummary("Atualiza os dados de um quarto existente")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);


            // ❌ REMOVER QUARTO (apenas admin)
            group.MapDelete("/{id}", async (int id, AppDbContext db) =>
            {
                var quarto = await db.Quartos
                    .Include(q => q.Locacoes)
                    .FirstOrDefaultAsync(q => q.Id == id);

                if (quarto == null)
                    return Results.NotFound("Quarto não encontrado.");

                if (quarto.Locacoes?.Any(l => l.Status == "Ativo" || l.Status == "Reservado") ?? false)
        return Results.BadRequest("Não é possível excluir quarto com locações ativas ou reservas futuras.");

                db.Quartos.Remove(quarto);
                await db.SaveChangesAsync();

                return Results.NoContent();
            })
            .RequireAuthorization("admin")
            .WithSummary("Exclui um quarto existente")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

            // 🛏️ VERIFICAR DISPONIBILIDADE DO QUARTO EM UM PERÍODO ESPECÍFICO
            group.MapGet("/disponibilidade/{id}", async (
                int id,
                [FromQuery] DateTime dataInicio,
                [FromQuery] DateTime dataFim,
                AppDbContext db,
                [FromQuery] int? excluirReserva = null) => // 🔥 MOVER PARA O FINAL
            {
                var quarto = await db.Quartos.FindAsync(id);
                if (quarto == null)
                    return Results.NotFound("Quarto não encontrado.");

                // Converter para UTC para comparação correta
                var dataInicioUtc = dataInicio.ToUniversalTime().Date;
                var dataFimUtc = dataFim.ToUniversalTime().Date;

                // Status válidos para considerar ocupação (MESMA LÓGICA DO LocacaoEndpoints)
                var statusValidos = new[] { "Reservado", "Ativo" };

                // Query para locações conflitantes (MESMA LÓGICA DO LocacaoEndpoints)
                var query = db.Locacoes
                    .Where(l => l.QuartoId == id &&
                               statusValidos.Contains(l.Status) &&
                               l.DataEntrada < dataFimUtc &&
                               l.DataSaida > dataInicioUtc);

                // Excluir uma reserva específica (útil para edição)
                if (excluirReserva.HasValue)
                {
                    query = query.Where(l => l.Id != excluirReserva.Value);
                }

                var conflitos = await query.ToListAsync();

                // Calcular camas ocupadas (MESMA LÓGICA DO LocacaoEndpoints)
                var camasOcupadas = conflitos.Sum(l =>
                    l.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : l.QuantidadeCamas);

                // Determinar status
                var status = "Disponível";
                if (camasOcupadas == quarto.QuantidadeCamas)
                    status = "Ocupado";
                else if (camasOcupadas > 0)
                    status = "Parcialmente Ocupado";

                return Results.Ok(new
                {
                    Id = quarto.Id,
                    Numero = quarto.Numero,
                    Grupo = quarto.Grupo,
                    TotalCamas = quarto.QuantidadeCamas,
                    CamasOcupadas = camasOcupadas,
                    CamasDisponiveis = quarto.QuantidadeCamas - camasOcupadas,
                    Status = status,
                    Periodo = new { DataInicio = dataInicioUtc, DataFim = dataFimUtc },
                    Conflitos = conflitos.Count
                });
            })
            .WithSummary("Verifica a disponibilidade de um quarto em um período específico")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🛏️ DETALHAR OCUPAÇÃO DO QUARTO
        group.MapGet("/ocupacao/{id}", async (int id, AppDbContext db) =>
{
    var quarto = await db.Quartos
        .Include(q => q.Locacoes)
        .FirstOrDefaultAsync(q => q.Id == id);

    if (quarto == null)
        return Results.NotFound("Quarto não encontrado.");

    // ✅ CORREÇÃO: Considera APENAS locações ATIVAS para calcular camas ocupadas
    var camasOcupadas = quarto.Locacoes?
        .Where(l => l.Status == "Ativo" && // ← APENAS "Ativo"!
                    l.DataEntrada <= DateTime.Today &&
                    l.DataSaida >= DateTime.Today)
        .Sum(l => l.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : l.QuantidadeCamas) ?? 0;

    var status = "Disponível";
    if (camasOcupadas == quarto.QuantidadeCamas)
        status = "Ocupado";
    else if (camasOcupadas > 0)
        status = "Parcialmente Ocupado";

    var ocupacaoDto = new QuartoOcupacaoDTO
    {
        Numero = quarto.Numero,
        Grupo = quarto.Grupo,
        TotalCamas = quarto.QuantidadeCamas,
        CamasOcupadas = camasOcupadas,
        Status = status
    };

    return Results.Ok(ocupacaoDto);
})
.WithSummary("Obtém o status de ocupação detalhado de um quarto")
.Produces<QuartoOcupacaoDTO>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status401Unauthorized);

        }
    }
}
