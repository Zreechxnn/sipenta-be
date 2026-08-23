using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SIAP.Api.Common;
using SIAP.Api.DTOs;
using SIAP.Api.Entities;
using SIAP.Api.Hubs;
using SIAP.Api.Repositories.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BidangController : ControllerBase
{
    private readonly IBidangRepository _bidangRepository;
    private readonly IHubContext<AppHub> _hubContext;

    public BidangController(IBidangRepository bidangRepository, IHubContext<AppHub> hubContext)
    {
        _bidangRepository = bidangRepository;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var bidangs = await _bidangRepository.GetAllAsync();
            var dtos = bidangs.Select(b => new BidangDto
            {
                Id = b.Id,
                Nama = b.Nama,
                Kode = b.Kode,
                Deskripsi = b.Deskripsi,
                UserCount = b.Users?.Count ?? 0,
                DocumentCount = b.Documents?.Count ?? 0,
                CreatedAt = b.CreatedAt
            }).ToList();

            return Ok(ApiResponse<List<BidangDto>>.Ok(dtos));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<List<BidangDto>>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var b = await _bidangRepository.GetByIdAsync(id);
            if (b == null)
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Bidang tidak ditemukan." });

            var dto = new BidangDto
            {
                Id = b.Id,
                Nama = b.Nama,
                Kode = b.Kode,
                Deskripsi = b.Deskripsi,
                UserCount = b.Users?.Count ?? 0,
                DocumentCount = b.Documents?.Count ?? 0,
                CreatedAt = b.CreatedAt
            };

            return Ok(ApiResponse<BidangDto>.Ok(dto));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<BidangDto>.Gagal(ex.Message));
        }
    }

    [HttpPost]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> Create([FromBody] CreateBidangRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Nama))
                return BadRequest(ApiResponse<BidangDto>.Gagal("Nama bidang wajib diisi."));

            var existing = await _bidangRepository.GetByNamaAsync(request.Nama);
            if (existing != null)
                return BadRequest(ApiResponse<BidangDto>.Gagal("Nama atau kode bidang sudah terdaftar."));

            var bidang = new Bidang
            {
                Nama = request.Nama.Trim(),
                Kode = request.Kode?.Trim().ToUpper(),
                Deskripsi = request.Deskripsi?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            var created = await _bidangRepository.AddAsync(bidang);

            var dto = new BidangDto
            {
                Id = created.Id,
                Nama = created.Nama,
                Kode = created.Kode,
                Deskripsi = created.Deskripsi,
                UserCount = 0,
                DocumentCount = 0,
                CreatedAt = created.CreatedAt
            };

            await _hubContext.Clients.All.SendAsync("BidangCreated", dto);

            return CreatedAtAction(nameof(GetById), new { id = dto.Id }, ApiResponse<BidangDto>.Ok(dto, "Bidang berhasil ditambahkan."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<BidangDto>.Gagal(ex.Message));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBidangRequest request)
    {
        try
        {
            var bidang = await _bidangRepository.GetByIdAsync(id);
            if (bidang == null)
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Bidang tidak ditemukan." });

            if (string.IsNullOrWhiteSpace(request.Nama))
                return BadRequest(ApiResponse<BidangDto>.Gagal("Nama bidang wajib diisi."));

            bidang.Nama = request.Nama.Trim();
            bidang.Kode = request.Kode?.Trim().ToUpper();
            bidang.Deskripsi = request.Deskripsi?.Trim();

            await _bidangRepository.UpdateAsync(bidang);

            var dto = new BidangDto
            {
                Id = bidang.Id,
                Nama = bidang.Nama,
                Kode = bidang.Kode,
                Deskripsi = bidang.Deskripsi,
                UserCount = bidang.Users?.Count ?? 0,
                DocumentCount = bidang.Documents?.Count ?? 0,
                CreatedAt = bidang.CreatedAt
            };

            await _hubContext.Clients.All.SendAsync("BidangUpdated", dto);

            return Ok(ApiResponse<BidangDto>.Ok(dto, "Bidang berhasil diperbarui."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<BidangDto>.Gagal(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var bidang = await _bidangRepository.GetByIdAsync(id);
            if (bidang == null)
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Bidang tidak ditemukan." });

            await _bidangRepository.DeleteAsync(bidang);

            await _hubContext.Clients.All.SendAsync("BidangDeleted", id);

            return Ok(ApiResponse<bool>.Ok(true, "Bidang berhasil dihapus."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<bool>.Gagal(ex.Message));
        }
    }
}
