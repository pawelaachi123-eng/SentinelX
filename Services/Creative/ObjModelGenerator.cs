using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SentinelX.Services.History;

namespace SentinelX.Services.Creative;

public sealed record ObjModelSpec(string Shape, double Width, double Height, double Depth, double Radius, int Segments, int Rings);
public sealed record ObjModelResult(bool Success, string Message, string Path, string Sha256, int Vertices, int Faces);

/// <summary>Creates small, deterministic Wavefront OBJ meshes in Sentinel's own data folder.
/// It accepts no output path and never starts Blender, Roblox Studio, or another process.</summary>
public sealed class ObjModelGenerator
{
    private const double MinimumDimension = 0.001;
    private const double MaximumDimension = 100000;
    private const int MinimumSegments = 8;
    private const int MaximumSegments = 96;
    private readonly string outputDirectory;

    private static readonly Regex CommandPattern = new(
        @"^\s*(?:(?:zbuduj|generuj|stworz|utworz|zrob)\s+)?(?:(?:model\s+3d)|(?:modeluj\s+3d)|(?:druk\s+3d))(?:\s+obj)?\s*(?::\s*(?<args>[\s\S]*))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public ObjModelGenerator() : this(Path.Combine(AppPaths.Root, "CreatedModels")) { }

    internal ObjModelGenerator(string outputDirectory)
    {
        this.outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public string OutputDirectory => outputDirectory;

    public static bool IsCommand(string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        try { return CommandPattern.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool TryParseCommand(string command, out ObjModelSpec spec, out string message)
    {
        spec = new("", 0, 0, 0, 0, 0, 0);
        message = Usage;
        string normalizedCommand = ConversationMemoryService.Normalize(command ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        Match match;
        try { match = CommandPattern.Match(normalizedCommand); }
        catch (RegexMatchTimeoutException) { return false; }
        if (!match.Success) return false;

        string arguments = match.Groups["args"].Value.Trim();
        if (arguments.Length == 0) return false;
        string[] parts = Regex.Split(arguments, @"\s+");
        if (parts.Length > 4)
        {
            message = "Każdy kształt przyjmuje najwyżej trzy wartości liczbowe.\n" + Usage;
            return false;
        }
        string shape = ConversationMemoryService.Normalize(parts[0]).Trim().ToLowerInvariant() switch
        {
            "cube" or "box" or "kostka" or "klocek" => "cube",
            "plane" or "plaszczyzna" or "platforma" => "plane",
            "sphere" or "sfera" or "kula" => "sphere",
            "cylinder" or "walec" => "cylinder",
            "cone" or "stozek" => "cone",
            _ => ""
        };
        if (shape.Length == 0)
        {
            message = "Nie znam takiej bryły. Dostępne: cube, plane, sphere, cylinder, cone (także: kostka, kula, walec, stożek).\n" + Usage;
            return false;
        }

        string[] values = parts.Skip(1).ToArray();
        if (values.Any(value => !TryNumber(value, out _)))
        {
            message = "Wymiary muszą być liczbami dodatnimi, np. „model 3d: cube 2 1 3”.\n" + Usage;
            return false;
        }
        double[] numbers = values.Select(value => TryNumber(value, out double number) ? number : double.NaN).ToArray();
        double Get(int index, double fallback) => index < numbers.Length ? numbers[index] : fallback;

        double width = 2, height = 2, depth = 2, radius = 1;
        int segments = 24, rings = 12;
        switch (shape)
        {
            case "cube":
                if (numbers.Length is not (0 or 1 or 3)) { message = "Kostka przyjmuje zero, jeden (bok) albo trzy wymiary: cube [bok] lub cube szerokość wysokość głębokość."; return false; }
                width = numbers.Length == 1 ? numbers[0] : Get(0, 2);
                height = numbers.Length == 1 ? numbers[0] : Get(1, 2);
                depth = numbers.Length == 1 ? numbers[0] : Get(2, 2);
                break;
            case "plane":
                if (numbers.Length is not (0 or 1 or 2)) { message = "Płaszczyzna przyjmuje zero, jeden (bok) albo dwa wymiary: plane [szerokość głębokość]."; return false; }
                width = numbers.Length == 1 ? numbers[0] : Get(0, 8);
                depth = numbers.Length == 1 ? numbers[0] : Get(1, 8);
                break;
            case "sphere":
                if (numbers.Length > 3 || (numbers.Length >= 2 && !TryInteger(values[1], out _)) || (numbers.Length >= 3 && !TryInteger(values[2], out _)))
                { message = "Sfera: sphere [promień [segmenty [pierścienie]]], np. „sphere 1 24 12”."; return false; }
                radius = Get(0, 1);
                if (numbers.Length >= 2) TryInteger(values[1], out segments);
                if (numbers.Length >= 3) TryInteger(values[2], out rings);
                break;
            case "cylinder":
            case "cone":
                if (numbers.Length > 3 || (numbers.Length >= 3 && !TryInteger(values[2], out _)))
                { message = "Walec/stożek: radius [wysokość [segmenty]], np. „cylinder 1 2 24”."; return false; }
                radius = Get(0, 1);
                height = Get(1, 2);
                if (numbers.Length >= 3) TryInteger(values[2], out segments);
                break;
        }

        if (shape is "cube" or "plane")
        {
            if (!ValidDimension(width) || (shape == "cube" && !ValidDimension(height)) || !ValidDimension(depth))
            { message = "Wymiary muszą mieścić się w zakresie 0,001–100000 jednostek."; return false; }
        }
        else if (!ValidDimension(radius) || ((shape is "cylinder" or "cone") && !ValidDimension(height)))
        { message = "Promień i wysokość muszą mieścić się w zakresie 0,001–100000 jednostek."; return false; }

        if (segments is < MinimumSegments or > MaximumSegments)
        { message = $"Liczba segmentów musi mieścić się w zakresie {MinimumSegments}–{MaximumSegments}."; return false; }
        if (shape == "sphere" && (rings is < 4 or > MaximumSegments))
        { message = $"Sfera wymaga od 4 do {MaximumSegments} pierścieni."; return false; }

        spec = new(shape, width, height, depth, radius, segments, rings);
        message = "";
        return true;
    }

    public async Task<ObjModelResult> GenerateFromCommandAsync(string command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!TryParseCommand(command, out ObjModelSpec spec, out string message))
            return new(false, message, "", "", 0, 0);
        return await GenerateAsync(spec, command, token).ConfigureAwait(false);
    }

    public async Task<ObjModelResult> GenerateAsync(ObjModelSpec spec, string command = "model 3d", CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidSpec(spec, out string validationMessage))
            return new(false, validationMessage, "", "", 0, 0);
        Mesh mesh = BuildMesh(spec);
        string content = FormatObj(spec, mesh);
        string stem = "sentinel-" + spec.Shape + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        string path = Path.Combine(outputDirectory, stem + ".obj");
        string temporary = path + ".tmp";

        try
        {
            Directory.CreateDirectory(outputDirectory);
            if ((File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Folder docelowy jest dowiązaniem systemu plików i nie będzie użyty.");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            string readBack = await File.ReadAllTextAsync(temporary, Encoding.UTF8, token).ConfigureAwait(false);
            if (!string.Equals(readBack, content, StringComparison.Ordinal))
                throw new IOException("Odczyt kontrolny nie zgadza się z wygenerowaną siatką.");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(readBack))).ToLowerInvariant();
            File.Move(temporary, path);

            string evidence = $"{path}\nWierzchołki: {mesh.Vertices.Count}; ściany: {mesh.Faces.Count}; SHA-256: {hash}; odczyt kontrolny OBJ zgodny.";
            ActionEvidenceCapture.Record(new ActionHistoryEntry
            {
                ActionId = "SX-MODEL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Timestamp = DateTime.Now,
                ActionType = "MODEL_OBJ",
                Command = command,
                Status = "VERIFIED",
                Message = "Wygenerowano lokalną siatkę Wavefront OBJ.",
                Evidence = evidence,
                RecoveryAdvice = "Zaimportuj plik ręcznie w Blenderze albo innym importerze OBJ; najpierw sprawdź skalę i orientację osi."
            });

            string summary = "VERIFIED · Wygenerowano siatkę Wavefront OBJ „" + spec.Shape + "”\n" +
                $"Wierzchołki: {mesh.Vertices.Count}; ściany: {mesh.Faces.Count}.\n" +
                "Plik: " + path + "\nSHA-256: " + hash + "\n" +
                "Format OBJ jest tekstowy. Zaimportuj ręcznie w Blenderze lub innym importerze OBJ; Sentinel nie uruchamia aplikacji 3D ani nie weryfikuje renderu.";
            return new(true, summary, path, hash, mesh.Vertices.Count, mesh.Faces.Count);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TryDelete(temporary);
            return new(false, "Nie udało się bezpiecznie zapisać modelu OBJ: " + ex.Message, "", "", mesh.Vertices.Count, mesh.Faces.Count);
        }
    }

    private static bool ValidSpec(ObjModelSpec spec, out string message)
    {
        message = "Parametry modelu są poza obsługiwanym zakresem.";
        if (spec is null || string.IsNullOrWhiteSpace(spec.Shape) || (spec.Segments is < MinimumSegments or > MaximumSegments)) return false;
        return spec.Shape switch
        {
            "cube" => ValidDimension(spec.Width) && ValidDimension(spec.Height) && ValidDimension(spec.Depth),
            "plane" => ValidDimension(spec.Width) && ValidDimension(spec.Depth),
            "sphere" => ValidDimension(spec.Radius) && spec.Rings is >= 4 and <= MaximumSegments,
            "cylinder" or "cone" => ValidDimension(spec.Radius) && ValidDimension(spec.Height),
            _ => false
        };
    }

    private static Mesh BuildMesh(ObjModelSpec spec) => spec.Shape switch
    {
        "cube" => BuildCube(spec.Width, spec.Height, spec.Depth),
        "plane" => BuildPlane(spec.Width, spec.Depth),
        "sphere" => BuildSphere(spec.Radius, spec.Segments, spec.Rings),
        "cylinder" => BuildCylinder(spec.Radius, spec.Height, spec.Segments),
        "cone" => BuildCone(spec.Radius, spec.Height, spec.Segments),
        _ => throw new ArgumentException("Nieobsługiwany kształt OBJ.", nameof(spec))
    };

    private static Mesh BuildCube(double width, double height, double depth)
    {
        double x = width / 2, y = height / 2, z = depth / 2;
        var mesh = new Mesh();
        mesh.Vertices.AddRange([
            new(-x, -y, -z), new(x, -y, -z), new(x, y, -z), new(-x, y, -z),
            new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z)
        ]);
        mesh.Faces.AddRange([
            [4, 5, 6, 7], [1, 0, 3, 2], [5, 1, 2, 6], [0, 4, 7, 3], [7, 6, 2, 3], [0, 1, 5, 4]
        ]);
        return mesh;
    }

    private static Mesh BuildPlane(double width, double depth)
    {
        double x = width / 2, z = depth / 2;
        var mesh = new Mesh();
        mesh.Vertices.AddRange([new(-x, 0, -z), new(x, 0, -z), new(x, 0, z), new(-x, 0, z)]);
        mesh.Faces.Add([0, 3, 2, 1]);
        return mesh;
    }

    private static Mesh BuildCylinder(double radius, double height, int segments)
    {
        var mesh = new Mesh();
        double bottom = -height / 2, top = height / 2;
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            double x = radius * Math.Cos(angle), z = radius * Math.Sin(angle);
            mesh.Vertices.Add(new(x, bottom, z));
        }
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            double x = radius * Math.Cos(angle), z = radius * Math.Sin(angle);
            mesh.Vertices.Add(new(x, top, z));
        }
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            mesh.Faces.Add([i, segments + i, segments + next, next]);
        }
        mesh.Faces.Add(Enumerable.Range(0, segments).ToArray());
        mesh.Faces.Add(Enumerable.Range(0, segments).Reverse().Select(i => segments + i).ToArray());
        return mesh;
    }

    private static Mesh BuildCone(double radius, double height, int segments)
    {
        var mesh = new Mesh();
        double bottom = -height / 2;
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            mesh.Vertices.Add(new(radius * Math.Cos(angle), bottom, radius * Math.Sin(angle)));
        }
        int tip = mesh.Vertices.Count;
        mesh.Vertices.Add(new(0, height / 2, 0));
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            mesh.Faces.Add([i, tip, next]);
        }
        mesh.Faces.Add(Enumerable.Range(0, segments).ToArray());
        return mesh;
    }

    private static Mesh BuildSphere(double radius, int segments, int rings)
    {
        var mesh = new Mesh();
        int top = mesh.Vertices.Count;
        mesh.Vertices.Add(new(0, radius, 0));
        for (int ring = 1; ring < rings; ring++)
        {
            double latitude = Math.PI * ring / rings;
            double y = radius * Math.Cos(latitude);
            double ringRadius = radius * Math.Sin(latitude);
            for (int segment = 0; segment < segments; segment++)
            {
                double longitude = 2 * Math.PI * segment / segments;
                mesh.Vertices.Add(new(ringRadius * Math.Cos(longitude), y, ringRadius * Math.Sin(longitude)));
            }
        }
        int bottom = mesh.Vertices.Count;
        mesh.Vertices.Add(new(0, -radius, 0));
        int RingVertex(int ring, int segment) => 1 + (ring - 1) * segments + segment % segments;

        for (int segment = 0; segment < segments; segment++)
        {
            int current = RingVertex(1, segment), next = RingVertex(1, segment + 1);
            mesh.Faces.Add([top, next, current]);
        }
        for (int ring = 1; ring < rings - 1; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int current = RingVertex(ring, segment);
                int next = RingVertex(ring, segment + 1);
                int lowerNext = RingVertex(ring + 1, segment + 1);
                int lower = RingVertex(ring + 1, segment);
                mesh.Faces.Add([current, next, lowerNext, lower]);
            }
        }
        int lastRing = rings - 1;
        for (int segment = 0; segment < segments; segment++)
            mesh.Faces.Add([bottom, RingVertex(lastRing, segment), RingVertex(lastRing, segment + 1)]);
        return mesh;
    }

    private static string FormatObj(ObjModelSpec spec, Mesh mesh)
    {
        var builder = new StringBuilder(Math.Max(256, mesh.Vertices.Count * 48));
        builder.AppendLine("# Generated locally by Sentinel X. No renderer or external process was used.");
        builder.AppendLine("# Wavefront OBJ; dimensions are unitless and the origin is centered.");
        builder.Append("o sentinel_").AppendLine(spec.Shape);
        foreach (Vertex vertex in mesh.Vertices)
            builder.Append("v ").Append(Format(vertex.X)).Append(' ').Append(Format(vertex.Y)).Append(' ').AppendLine(Format(vertex.Z));
        foreach (int[] face in mesh.Faces)
        {
            builder.Append('f');
            foreach (int index in face) builder.Append(' ').Append(index + 1);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static bool TryInteger(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static bool ValidDimension(double value) => double.IsFinite(value) && value is >= MinimumDimension and <= MaximumDimension;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }

    private sealed class Mesh
    {
        public List<Vertex> Vertices { get; } = [];
        public List<int[]> Faces { get; } = [];
    }

    private readonly record struct Vertex(double X, double Y, double Z);

    private const string Usage = "Utwórz prostą siatkę OBJ poleceniem „model 3d: cube 2 2 2”. Kształty: cube [bok lub szerokość wysokość głębokość], plane [szerokość głębokość], sphere [promień segmenty pierścienie], cylinder/cone [promień wysokość segmenty]. Zakres segmentów: 8–96. Zapis trafia wyłącznie do folderu danych SentinelX\\CreatedModels; ścieżki z polecenia nie są przyjmowane.";
}
