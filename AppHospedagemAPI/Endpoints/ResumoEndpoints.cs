using AppHospedagemAPI.Data;
using AppHospedagemAPI.DTOs; // Para DashboardResumoResponse
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc; // Para StatusCodes
using System.Linq; // Para ToLower() e GroupBy

namespace AppHospedagemAPI.Endpoints
{
    public static class ResumoEndpoints
    {
        public static void MapResumoEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/dashboard") // Criando grupo /dashboard
                .WithTags("Dashboard") // Tag para Swagger
                .RequireAuthorization("admin", "gerente"); // Apenas admin e gerente podem ver o resumo

            group.MapGet("/resumo", async (AppDbContext db) =>
            {
                var dataAtual = DateTime.Today;

                // --- Calcular Quartos Ocupados (totalmente ou parcialmente) ---
                // Precisamos carregar quartos com suas locações ativas para calcular ocupação por quarto
                var quartosComOcupacao = await db.Quartos
                    .Include(q => q.Locacoes.Where(l =>
                        l.DataEntrada <= dataAtual && l.DataSaida >= dataAtual &&
                        (l.Status == "reservado" || l.Status == "ativo"))) // Considera reservas e ativas
                    .ToListAsync();

                int quartosOcupadosTotalmente = 0;
                int quartosParcialmenteOcupados = 0;

                foreach (var quarto in quartosComOcupacao)
                {
                    // Soma as camas ocupadas para o dia de hoje, considerando tipo 'quarto' ou 'cama'
                    int camasOcupadas = quarto.Locacoes
                        .Sum(l => l.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : l.QuantidadeCamas);

                    if (camasOcupadas == quarto.QuantidadeCamas && quarto.QuantidadeCamas > 0)
                    {
                        quartosOcupadosTotalmente++;
                    }
                    else if (camasOcupadas > 0 && camasOcupadas < quarto.QuantidadeCamas)
                    {
                        quartosParcialmenteOcupados++;
                    }
                }

                // --- Outras Métricas ---

                // Reservas esperadas para Check-in hoje
                var reservasHoje = await db.Locacoes
                    .CountAsync(l => l.DataEntrada.Date == dataAtual && l.Status == "reservado");

                // Locações para Check-out hoje (que já fizeram check-in e estão ativas)
                var checkOutsHoje = await db.Locacoes
                    .CountAsync(l => l.DataSaida.Date == dataAtual && l.Status == "ativo" && l.CheckInRealizado);

                // Clientes Ativos (com locação "ativo" ou "reservado" hoje)
                var clientesAtivosHoje = await db.Locacoes
                    .Where(l => l.DataEntrada <= dataAtual && l.DataSaida >= dataAtual &&
                                (l.Status == "ativo" || l.Status == "reservado"))
                    .Select(l => l.ClienteId)
                    .Distinct()
                    .CountAsync();

                var totalQuartos = await db.Quartos.CountAsync();
                var quartosLivres = totalQuartos - (quartosOcupadosTotalmente + quartosParcialmenteOcupados);


                return Results.Ok(new DashboardResumoResponse
                {
                    TotalQuartos = totalQuartos,
                    QuartosLivres = quartosLivres,
                    QuartosOcupadosTotalmente = quartosOcupadosTotalmente,
                    QuartosParcialmenteOcupados = quartosParcialmenteOcupados,
                    ReservasHoje = reservasHoje,
                    CheckOutsHoje = checkOutsHoje,
                    ClientesAtivosHoje = clientesAtivosHoje
                });
            })
            .WithSummary("Obtém um resumo de estatísticas para o dashboard.")
            .WithDescription("Fornece informações sobre ocupação de quartos, reservas e clientes ativos para o dia atual.")
            .Produces<DashboardResumoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }
}