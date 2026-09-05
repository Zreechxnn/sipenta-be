namespace SIAP.Api.Common;

public class ApiResponse<T>
{
    public bool Sukses { get; set; }
    public string? Pesan { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    public static ApiResponse<T> Ok(T data, string? pesan = null) =>
        new() { Sukses = true, Pesan = pesan ?? "Berhasil", Data = data };

    public static ApiResponse<T> Gagal(string pesan, List<string>? errors = null) =>
        new() { Sukses = false, Pesan = pesan, Errors = errors };
}