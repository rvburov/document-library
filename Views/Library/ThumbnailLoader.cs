using Avalonia.Media.Imaging;
using PdfLibrary.Core.Documents;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Миниатюры первых страниц для карточек ленты и списка панели чтения. Картинка рисуется один
/// раз и ложится файлом в папку миниатюр (<see cref="DocThumbnail"/>); здесь — готовые картинки
/// в памяти для экрана, с потолком по байтам (<see cref="DocLibraryLimits.ThumbnailCacheBytes"/>):
/// лента прокручивается через тысячи документов, и держать все картинки нельзя.
///
/// Рисование PDF идет по одному (PDFium сериализован <see cref="PdfGate"/>), поэтому очередь
/// ограничена двумя задачами: третья только ждала бы замка, занимая поток пула.
/// </summary>
public static class ThumbnailLoader
{
    private static readonly SemaphoreSlim Gate = new(2, 2);
    private static readonly object Lock = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap, long Bytes)>> Map = new();
    private static readonly LinkedList<(string Key, Bitmap Bitmap, long Bytes)> Order = new();
    private static readonly Dictionary<string, Task<Bitmap?>> Pending = new();
    private static long _bytes;

    /// <summary>Готовая картинка из памяти без похода на диск; null — еще не загружена.</summary>
    public static Bitmap? Peek(string path, long mtime, long size)
    {
        lock (Lock)
        {
            if (!Map.TryGetValue(KeyOf(path, mtime, size), out var node)) return null;
            Order.Remove(node);
            Order.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    /// <summary>Картинка первой страницы; null — документ не нарисовался (битый, пропал).</summary>
    public static Task<Bitmap?> GetAsync(string path, long mtime, long size)
    {
        var key = KeyOf(path, mtime, size);
        lock (Lock)
        {
            if (Map.TryGetValue(key, out var node)) return Task.FromResult<Bitmap?>(node.Value.Bitmap);
            if (Pending.TryGetValue(key, out var running)) return running;
            var task = Task.Run(() => Load(key, path, mtime, size));
            Pending[key] = task;
            return task;
        }
    }

    private static async Task<Bitmap?> Load(string key, string path, long mtime, long size)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var file = DocThumbnail.Cached(AppSettings.ThumbsDir, path, mtime, size)
                       ?? DocThumbnail.Render(AppSettings.ThumbsDir, path);
            if (file is null) return null;

            var bitmap = new Bitmap(file);
            var bytes  = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            lock (Lock)
            {
                Order.AddFirst((key, bitmap, bytes));
                Map[key] = Order.First!;
                _bytes += bytes;
                // Старые картинки уходят из памяти, но не уничтожаются: карточка, которая их еще
                // показывает, должна дорисоваться. Сборщик мусора заберет их сам.
                while (_bytes > DocLibraryLimits.ThumbnailCacheBytes && Order.Last is { } last && last != Order.First)
                {
                    Order.RemoveLast();
                    Map.Remove(last.Value.Key);
                    _bytes -= last.Value.Bytes;
                }
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
            lock (Lock) Pending.Remove(key);
        }
    }

    /// <summary>Забыть картинки в памяти — после «Очистить» кеш документов.</summary>
    public static void Forget()
    {
        lock (Lock)
        {
            Map.Clear();
            Order.Clear();
            _bytes = 0;
        }
    }

    private static string KeyOf(string path, long mtime, long size) => $"{path}|{mtime}|{size}";
}
