# Kumpulan Diagram Arsitektur Mendalam SIAP / SIPENTA

Dokumen ini berisi visualisasi teknis mendalam (*Sequence, Activity, Flowchart, Component*) sesuai dengan implementasi kode terbaru pada sistem **SIAP / SIPENTA** (*.NET 10 & Next.js*), memetakan interaksi, penanganan kegagalan, percabangan logika, hingga proses AI dan Google Drive.

---

## 1. Sequence Diagram: Pipeline Pemrosesan Dokumen Lanjutan

Sequence diagram ini menggambarkan interaksi asinkron dari pengunggahan berkas, penyimpanan Google Drive, ekstraksi teks & OCR, ekstraksi metadata LLM 3.000 karakter, ekstraksi gambar paralel (`Task.WhenAll`), hingga pemecahan teks (*chunking*).

```mermaid
sequenceDiagram
    autonumber
    actor Pengguna as Pegawai / Kepala Bagian / Super Admin
    participant HTTP as Controllers & Middleware
    participant DS as DocumentService
    participant GDRV as GoogleDriveService (Cloud)
    participant REPO as DocumentRepository
    participant QUEUE as DocumentProcessingQueue
    participant DB as PostgreSQL (pgvector)
    participant BG as DocumentProcessingService (Background Worker)
    participant DETECT as DocumentDetectionService
    participant PARSER as DocumentParserFactory (Pdf/Docx/Txt)
    participant OCR as TesseractOcrProvider (OCR)
    participant GROQ as GroqService (LLM 3000 chars)
    participant IMG as PdfImageExtractor (iText 7)
    participant CHUNK as ChunkService
    participant HUB as SignalR AppHub (Real-time)

    Pengguna->>+HTTP: POST /api/Documents (multipart/form-data)
    HTTP->>+DS: UploadAsync(request)
    DS->>+GDRV: UploadFileAsync(stream, fileName)
    GDRV-->>-DS: Google Drive File ID
    DS->>+REPO: AddAsync(Document, Content)
    REPO->>DB: INSERT INTO "Documents" (Path = DriveFileId, Status = Uploaded)
    DB-->>REPO: Inserted
    REPO-->>-DS: Entitas tersimpan
    DS->>QUEUE: EnqueueAsync(documentId)
    DS-->>-HTTP: DocumentResponseDto[]
    HTTP-->>-Pengguna: 200 OK (Dokumen masuk antrean latar belakang)

    Note over QUEUE, BG: Eksekusi Latar Belakang (Asinkron & Terpisah)
    QUEUE->>+BG: DequeueAsync(CancellationToken)
    BG->>+REPO: GetByIdAsync(documentId)
    REPO->>DB: SELECT "Documents" JOIN "DocumentContents"
    DB-->>REPO: data
    REPO-->>-BG: document

    alt document == null
        BG-->>BG: Log Warning & Batalkan Pemrosesan
    else document != null
        BG->>DB: UPDATE Status = Processing, ProcessingStartedAt = UtcNow
        BG->>HUB: Broadcast DocumentUpdated (Processing)

        BG->>+GDRV: DownloadFileAsync(document.Path)
        GDRV-->>-BG: FileStream
        
        BG->>+DETECT: DetectAsync(fileStream, extension, mimeType)
        DETECT-->>-BG: DetectionResult (HasTextLayer, IsScannedDocument, IsImage)
        BG->>DB: UPDATE DocumentContent (HasTextLayer, IsScanned)

        alt HasTextLayer == true && IsImage == false (Dokumen Digital)
            BG->>+PARSER: GetParser(extension, mimeType)
            PARSER-->>BG: IDocumentParser (mis. PdfDocumentParser)
            BG->>PARSER: ParseAsync(fileStream)
            PARSER-->>-BG: ExtractionResult (RawText, PageCount)
            BG->>DB: UPDATE ParseStatus = Completed, OcrStatus = Skipped
        else Dokumen Scan atau Gambar
            BG->>DB: UPDATE OcrStatus = Processing, OcrStartedAt = UtcNow
            BG->>+OCR: ExtractTextAsync(fileStream)
            alt OCR Sukses
                OCR-->>BG: OcrResult (Success = true, RawText, Confidence)
                BG->>DB: UPDATE OcrStatus = Completed, ParseStatus = Completed, Engine = Tesseract
            else OCR Gagal
                OCR-->>-BG: OcrResult (Success = false, ErrorMessage)
                BG->>DB: UPDATE OcrStatus = Failed, ParseStatus = Failed, Status = Failed
            end
        end

        alt Status != Failed (Teks Berhasil Diekstrak)
            Note over BG, GROQ: 1. Ekstraksi Metadata via LLM (3.000 Karakter Pertama)
            BG->>+GROQ: GetChatCompletionAsync(systemPrompt, rawText[0..3000])
            GROQ-->>-BG: JSON Metadata (Judul, NamaTenagaAhli, JenisDokumen, PeriodeLaporan)
            BG->>BG: Deserialisasi JSON & Fallback Regex jika ada field kosong
            BG->>DB: UPDATE Metadata Dokumen

            Note over BG, IMG: 2. Ekstraksi Gambar PDF & Upload Paralel ke Google Drive
            BG->>+IMG: ExtractImagesAsync(fileStream)
            IMG-->>-BG: extractedImages[]
            alt Ada Gambar Terdeteksi
                Note over BG, GDRV: Eksekusi Upload Konkuren Menggunakan Task.WhenAll
                BG->>GDRV: UploadFileBytesAsync(...) [Paralel Task.WhenAll]
                GDRV-->>BG: Drive Image IDs
                BG->>DB: Batch INSERT INTO "DocumentImages"
            end

            Note over BG, CHUNK: 3. Pemotongan Teks (Chunking) & Pembangkitan Vektor
            BG->>+CHUNK: ProcessChunksAsync(documentId, null)
            CHUNK->>DB: DELETE FROM "DocumentChunks" WHERE DocumentId = doc.Id
            CHUNK->>CHUNK: Potong Teks per Bab/Paragraf & Buat Embedding Vektor
            CHUNK->>DB: INSERT INTO "DocumentChunks"
            CHUNK-->>-BG: Selesai Chunking

            BG->>DB: UPDATE Status = Parsed, ProcessingFinishedAt = UtcNow
            BG->>HUB: Broadcast DocumentUpdated (Parsed)
            HUB-->>Pengguna: Tabel terupdate real-time dengan status hijau (Selesai)
        else
            BG->>DB: Catat ErrorMessage & UPDATE Status = Failed
            BG->>HUB: Broadcast DocumentUpdated (Failed)
        end
    end
    BG-->>-QUEUE: Selesai proses item, tunggu dokumen berikutnya
```

---

## 2. Sequence Diagram: Pipeline Tanya Jawab RAG & Asisten AI

Memvisualisasikan alur Request dari Klien, verifikasi Token JWT, pemeriksaan Hak Akses Bidang/Super Admin, perumusan kata kunci cerdas (Groq LLM), pencarian Hibrida RRF (Reciprocal Rank Fusion), seleksi foto kegiatan bertanggal, hingga sintesis jawaban akhir.

```mermaid
sequenceDiagram
    autonumber
    actor Pengguna as Pimpinan / Kepala Bagian / Tenaga Ahli
    participant FE as Tampilan Chat Web (/chat)
    participant CTRL as ChatController
    participant AI as GroqService (LLM)
    participant EMBED as EmbeddingService
    participant REPO as DocumentRepository
    participant DB as PostgreSQL (pgvector)

    Pengguna->>FE: Ajukan pertanyaan: "Apa kegiatan Firman pada 5 Mei?"
    FE->>+CTRL: POST /api/Chat { Message, SessionId? }
    
    CTRL->>DB: Ambil profil user & peran (Super Admin / Kepala Bagian / User)
    
    alt SessionId == null (Topik Baru)
        CTRL->>DB: INSERT INTO "ChatSessions" (Title = 47 chars pertama)
    else Ada Sesi Lama
        CTRL->>DB: Ambil 6 pesan terakhir untuk konteks riwayat
    end

    Note over CTRL, AI: 1. Query Rewriting (Normalisasi Typo & Ekstraksi Entitas)
    CTRL->>+AI: Rumuskan 3-5 kata kunci dari pertanyaan & riwayat
    AI-->>-CTRL: SearchQuery: "Firman 5 Mei"

    Note over CTRL, EMBED: 2. Embedding Vektor
    CTRL->>+EMBED: GenerateEmbeddingAsync("Firman 5 Mei")
    EMBED-->>-CTRL: Vector 1536 Dimensi

    Note over CTRL, REPO: 3. Hybrid Search dengan Isolasi Bidang / Role
    CTRL->>+REPO: SearchHybridAsync(SearchQuery, Vector, TopK, UserId, BidangId, IsSuperAdmin)
    
    REPO->>DB: Vector Search (pgvector CosineDistance LIMIT TopK*3)
    DB-->>REPO: vectorResults[]
    REPO->>DB: Full-Text Search Bahasa Indonesia (ts_query AND/OR LIMIT TopK*3)
    DB-->>REPO: ftsResults[]
    REPO->>DB: Exact Substring ILike Search (Tanggal & Nama)
    DB-->>REPO: exactMatches[]
    
    REPO->>REPO: Algoritma RRF (Reciprocal Rank Fusion, k=60)
    REPO-->>-CTRL: List of Chunks Terpilih (Top-K)

    Note over CTRL, DB: 4. Seleksi Foto Kegiatan Berdasarkan Tanggal
    CTRL->>DB: SELECT * FROM "DocumentImages" WHERE DocumentId IN (docIds)
    DB-->>CTRL: allDocImages[]
    CTRL->>CTRL: Hitung skor kecocokan tanggal kegiatan (misal: "5 Mei") & keterangan
    
    Note over CTRL, AI: 5. Sintesis Konteks & Generasi Jawaban AI
    CTRL->>CTRL: Susun Prompt dengan tag: [Dokumen: Judul | Tenaga Ahli: Nama [Bidang: Nama]]
    CTRL->>DB: INSERT INTO "ChatMessages" (role = "user")
    
    CTRL->>+AI: GetChatCompletionWithHistoryAsync(SystemPrompt, Konteks, History)
    AI-->>-CTRL: Jawaban Rangkuman AI + Tautan Foto Dokumentasi Terpilih
    
    CTRL->>DB: INSERT INTO "ChatMessages" (role = "assistant", Sources = JSON)
    CTRL-->>-FE: 200 OK { SessionId, Answer, Sources }
    FE-->>Pengguna: Tampilkan jawaban rapi, foto dokumentasi, dan kartu dokumen rujukan
```

---

## 3. Flowchart: Algoritma Pencarian Hybrid (Reciprocal Rank Fusion)

Penjabaran teknis dari fungsi `SearchHybridAsync` yang berada pada repositori, menggabungkan pencarian semantik vektor, *Full-Text Search* bahasa Indonesia, dan pencocokan teks langsung:

```mermaid
flowchart TD
    START([Mulai: SearchHybridAsync]) --> VARS[Input: Keyword, Embedding Vector, TopK, UserId, BidangId, IsSuperAdmin]
    VARS --> FILTER_SECURITY{Pemeriksaan Hak Akses}

    FILTER_SECURITY -- "IsSuperAdmin == true" --> QUERY_ALL[Buka Seluruh Dokumen Lintas Bidang]
    FILTER_SECURITY -- "IsSuperAdmin == false" --> QUERY_SCOPED[Filter: Dokumen Sendiri + Dokumen 1 Bidang + Dokumen Shared]

    QUERY_ALL & QUERY_SCOPED --> MULTI[Set fetchCount = TopK x 3]
    MULTI --> PARALLEL_SEARCH{Jalankan 3 Metode Pencarian}

    subgraph Vector_Pipeline ["1. Pencarian Semantik Vektor"]
        PARALLEL_SEARCH --> V_QUERY[Kueri pgvector: ORDER BY CosineDistance]
        V_QUERY --> V_LIMIT[LIMIT fetchCount]
        V_LIMIT --> V_RES(vectorResults)
    end

    subgraph FTS_Pipeline ["2. Pencarian Full-Text Bahasa Indonesia"]
        PARALLEL_SEARCH --> FTS_PARSE[Ekstraksi Kata Kunci & Hapus Stopwords]
        FTS_PARSE --> FTS_AND[Coba Kueri AND: ToTsQuery 'indonesian']
        FTS_AND --> FTS_CHECK{Ada Hasil?}
        FTS_CHECK -- "Ya" --> FTS_RES(ftsResults)
        FTS_CHECK -- "Tidak" --> FTS_OR[Fallback ke Kueri OR: ToTsQuery]
        FTS_OR --> FTS_RES
    end

    subgraph Exact_Pipeline ["3. Pencocokan Substring Tepat"]
        PARALLEL_SEARCH --> EXACT_MATCH[ILike '%keyword%' pada Judul, Nama Tenaga Ahli, & Konten]
        EXACT_MATCH --> EXACT_RES(exactMatches)
    end

    V_RES & FTS_RES & EXACT_RES --> RRF_START

    subgraph RRF_Pipeline ["4. Reciprocal Rank Fusion (k=60)"]
        RRF_START[Inisialisasi Dictionary rrfScores] --> CALC_V[Skor Vektor: rrfScores += 1.0 / 60 + rank_v]
        CALC_V --> CALC_FTS[Skor FTS: rrfScores += 1.2 / 60 + rank_fts]
        CALC_FTS --> CALC_EXACT[Skor Exact: rrfScores += 1.5 / 60 + rank_exact]
        CALC_EXACT --> MERGE[Gabung & Deduplikasi Chunk Berdasarkan Id]
        MERGE --> SORT[Urutkan rrfScores secara Descending]
    end

    SORT --> FINAL_TAKE[Ambil Teratas: Take TopK]
    FINAL_TAKE --> END([Selesai: Kembalikan List DocumentChunk])
```

---

## 4. Flowchart: Pipeline Chunking Berdasarkan Strategi (Strategy Pattern)

Logika pemecahan teks laporan menjadi potongan-potongan kecil terstruktur sebelum disimpan ke database vektor:

```mermaid
flowchart TD
    START([Mulai Chunking Process]) --> INIT[Input: documentId, strategyName]
    INIT --> GETDOC[Ambil Dokumen dari Database]
    GETDOC --> CHECKTEXT{RawText Kosong?}
    CHECKTEXT -- "Ya" --> ERROR([Exception: Tidak ada teks untuk dipotong])
    CHECKTEXT -- "Tidak" --> DETECT_STRATEGY{strategyName Disuplai?}
    
    DETECT_STRATEGY -- "Ya" --> FACTORY
    DETECT_STRATEGY -- "Tidak" --> AUTO_EVAL{Jenis Dokumen Berupa Peraturan Hukum?}
    
    AUTO_EVAL -- "Ya (Perda / SK)" --> SET_LEGAL[strategyName = 'Legal']
    AUTO_EVAL -- "Tidak (Laporan Kerja Bulanan)" --> SET_PARA[strategyName = 'Paragraph']
    
    SET_LEGAL --> FACTORY
    SET_PARA --> FACTORY
    
    FACTORY[ChunkStrategyFactory.GetStrategy(strategyName)] --> CLEAR[DELETE Semua Chunk Lama untuk Dokumen ini]
    CLEAR --> EXECUTE[Eksekusi strategy.ChunkAsync]
    EXECUTE --> STRATEGY_TYPE{Strategi Terpilih}

    STRATEGY_TYPE -- "Legal" --> STR_LEGAL[Pemisahan Regex: BAB, PASAL, AYAT]
    STRATEGY_TYPE -- "Paragraph" --> STR_PARA[Pemisahan Baris Kosong + Overlap Karakter]
    STRATEGY_TYPE -- "Heading" --> STR_HEAD[Pemisahan pada Heading Markdown]
    STRATEGY_TYPE -- "Token" --> STR_TOKEN[Pemisahan Berdasarkan Estimasi maxTokens]

    STR_LEGAL & STR_PARA & STR_HEAD & STR_TOKEN --> VALIDATE_CHUNK{Ada Chunk Dihasilkan?}
    VALIDATE_CHUNK -- "Tidak" --> ERROR2([Exception: Chunk array kosong])
    VALIDATE_CHUNK -- "Ya" --> BUILD_CHUNK[Bentuk Entitas DocumentChunk: Hitung Kata, Offset, Serialisasi Metadata ke JSONB]
    
    BUILD_CHUNK --> SAVE[Simpan ke PostgreSQL & Bentuk Embedding]
    SAVE --> END([Berhasil: Chunks Disimpan])
```
