using System.ComponentModel.DataAnnotations;
using AppHospedagemAPI.ValidationAttributes;

namespace AppHospedagemAPI.DTOs;

public class LocacaoCreateRequest
{
    [Required(ErrorMessage = "O ID do cliente é obrigatório.")]
    public int ClienteId { get; set; }

    [Required(ErrorMessage = "O ID do quarto é obrigatório.")]
    public int QuartoId { get; set; }

    [Required(ErrorMessage = "A data de entrada é obrigatória.")]
    [DataType(DataType.Date)]
    // Exemplo de validação de data: A data de entrada não pode ser no passado (a menos que seja uma locação de hoje)
    [FutureOrToday(ErrorMessage = "A data de entrada não pode ser anterior à data atual.")]
    public DateTime DataEntrada { get; set; }

    [Required(ErrorMessage = "A data de saída é obrigatória.")]
    [DataType(DataType.Date)]
    // Validação: Data de saída deve ser posterior à data de entrada
    [DateGreaterThan("DataEntrada", ErrorMessage = "A data de saída deve ser posterior à data de entrada.")]
    public DateTime DataSaida { get; set; }

    [Required(ErrorMessage = "O tipo de locação (quarto/cama) é obrigatório.")]
    [StringLength(10, ErrorMessage = "Tipo de locação inválido.")] // "quarto" ou "cama"
    // Validação customizada para TipoLocacao (opcional, pode ser feito no endpoint também)
    [RegularExpression("^(quarto|cama)$", ErrorMessage = "Tipo de locação deve ser 'quarto' ou 'cama'.")]
    public string TipoLocacao { get; set; } = string.Empty;

    // Quantidade de camas é necessária apenas se TipoLocacao for "cama"
    [Range(1, 6, ErrorMessage = "Quantidade de camas inválida.")]
    // Custom validation attribute to make it required only if TipoLocacao is "cama"
    [RequiredIf("TipoLocacao", "cama", ErrorMessage = "Quantidade de camas é obrigatória para locação por cama.")]
    public int? QuantidadeCamas { get; set; } // Nullable, pois só é usado para "cama"

    // O preço total será calculado pela API, não enviado pelo cliente
}

