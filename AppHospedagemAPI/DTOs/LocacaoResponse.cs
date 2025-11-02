namespace AppHospedagemAPI.DTOs;

public class LocacaoResponse
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNome { get; set; } // Nome do cliente
    public int QuartoId { get; set; }
    public int QuartoNumero { get; set; } // Número do quarto
    public DateTime DataEntrada { get; set; }
    public DateTime DataSaida { get; set; }
    public string TipoLocacao { get; set; } = string.Empty;
    public int QuantidadeCamas { get; set; } // 0 se for locação de quarto
    public string Status { get; set; } = string.Empty; // Reservado, Ativo, Finalizado, Cancelado
    public bool CheckInRealizado { get; set; }
    public bool CheckOutRealizado { get; set; }
    //public decimal PrecoTotal { get; set; }
    public string? UsuarioResponsavelLogin { get; set; } // Login do usuário que criou/alterou
}

public class CheckoutRequest
{
    public DateTime? DataCheckout { get; set; }
}

public class LocacaoUpdateRequest
{
    public int? QuartoId { get; set; }
    public DateTime? DataEntrada { get; set; }  // 🔥 ADICIONAR ESTA LINHA
    public DateTime? DataSaida { get; set; }
    public string? TipoLocacao { get; set; }
    public int? QuantidadeCamas { get; set; }
}