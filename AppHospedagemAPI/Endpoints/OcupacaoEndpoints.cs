using AppHospedagemAPI.Data;
using AppHospedagemAPI.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace AppHospedagemAPI.Endpoints;

public static class OcupacaoEndpoints
{
    public static void MapOcupacaoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/ocupacao")
            .WithTags("Ocupação de Quartos")
            .RequireAuthorization();

        // 📋 Listar quartos com status de ocupação e filtros (VERSÃO CORRIGIDA)
        group.MapGet("/", async (
            [FromQuery] string? grupo,
            [FromQuery] string? status, // "Disponível", "Parcialmente Ocupado", "Ocupado"
            AppDbContext db) =>
        {
            var hoje = DateTime.Today;

            // ✅ CORREÇÃO: Status padronizados
            var statusAtivo = "Ativo";
            var statusReservado = "Reservado";
            var statusFinalizado = "Finalizado";
            var statusCancelado = "Cancelado";

            // Carrega quartos e locações
            var quartos = await db.Quartos
                .Include(q => q.Locacoes)
                .ToListAsync();

            var resultados = quartos.Select(quarto =>
            {
                // ✅ CORREÇÃO CRÍTICA: Considera APENAS locações ATIVAS para cálculo de ocupação
                int camasOcupadas = quarto.Locacoes?
                    .Where(l => l.DataEntrada <= hoje && 
                               l.DataSaida >= hoje && 
                               l.Status == statusAtivo) // ← APENAS LOCAÇÕES ATIVAS!
                    .Sum(l => l.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : l.QuantidadeCamas) ?? 0;

                string statusCalculado;

                if (camasOcupadas == 0)
                    statusCalculado = "Disponível";
                else if (camasOcupadas < quarto.QuantidadeCamas)
                    statusCalculado = "Parcialmente Ocupado";
                else
                    statusCalculado = "Ocupado";

                return new QuartoOcupacaoDTO
                {
                    Id = quarto.Id,
                    Numero = quarto.Numero,
                    Grupo = quarto.Grupo,
                    TotalCamas = quarto.QuantidadeCamas,
                    CamasOcupadas = camasOcupadas,
                    Status = statusCalculado
                };
            }).ToList();

            // Aplicar filtros em memória
            if (!string.IsNullOrEmpty(grupo))
            {
                resultados = resultados
                    .Where(q => q.Grupo.Equals(grupo, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrEmpty(status))
            {
                resultados = resultados
                    .Where(q => q.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return Results.Ok(resultados.OrderBy(q => q.Numero));
        })
        .WithSummary("Lista o status de ocupação atual de todos os quartos.")
        .WithDescription("Permite filtrar por grupo do quarto e status de ocupação (Disponível, Parcialmente Ocupado, Ocupado).")
        .Produces<IEnumerable<QuartoOcupacaoDTO>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}