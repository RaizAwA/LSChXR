// Procesamiento de imagen en C# PURO (sin OpenCV ni Unity) para el
// preprocesamiento de carteles del proyecto OCR-LSCh.
//
// Reimplementa el subconjunto de operaciones de OpenCV que usaba
// preprocesamiento.py: escala de grises, desenfoque gaussiano, Canny,
// dilatacion, contornos externos, minAreaRect, homografia + warp,
// Otsu, componentes conexos, cierre morfologico, CLAHE, y limpiezas.
//
// La logica de alto nivel (deteccion del cartel mas centrado, extrapolacion
// de esquinas, correccion fotometrica) es identica a la version Python/C#
// validada; solo cambia el motor de imagen.

using System;
using System.Collections.Generic;

namespace OcrLsch.Vision
{
    public sealed class ImagenColor   // BGR, fila-mayor, 3 bytes por pixel
    {
        public int Ancho, Alto;
        public byte[] Datos;
        public ImagenColor(int ancho, int alto)
        {
            Ancho = ancho; Alto = alto; Datos = new byte[ancho * alto * 3];
        }
    }

    public sealed class ImagenGris
    {
        public int Ancho, Alto;
        public byte[] Datos;
        public ImagenGris(int ancho, int alto)
        {
            Ancho = ancho; Alto = alto; Datos = new byte[ancho * alto];
        }
    }

    struct PtF { public double X, Y; public PtF(double x, double y) { X = x; Y = y; } }
    struct Rect { public int X, Y, W, H; public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; } }

    public static class PreprocesadorCartel
    {
        public const int AnchoSalida = 500;
        public const int AltoSalida = 700;

        const double FraccionAreaMinima = 0.02;
        const double AspectoMin = 0.45, AspectoMax = 0.95;
        const double FraccionAreaCompetencia = 0.30;
        const double DensidadBordesMinima = 0.02;

        public static ImagenGris PrepararCartel(ImagenColor imgBgr)
        {
            ImagenColor enderezado = CorregirPerspectiva(imgBgr);
            return MejorarParaOcr(enderezado);
        }

        // ---------------- Deteccion y seleccion ----------------

        sealed class Candidato
        {
            public List<PtF> Contorno;
            public double Area;
            public double Dist;
            public Rect Bbox;
        }

        static PtF[] DetectarCartel(ImagenColor img)
        {
            ImagenGris gris = Ops.AGris(img);
            ImagenGris blur = Ops.GaussianBlur5(gris);
            ImagenGris edges = Ops.Canny(blur, 50, 150);
            ImagenGris dilated = Ops.Dilatar(edges, 5, 2);

            List<List<PtF>> contornos = Ops.EncontrarContornosExternos(dilated);

            double areaImagen = (double)gris.Ancho * gris.Alto;
            var centroImg = new PtF(gris.Ancho / 2.0, gris.Alto / 2.0);
            double diagonal = Math.Sqrt((double)gris.Ancho * gris.Ancho + (double)gris.Alto * gris.Alto);

            var candidatos = new List<Candidato>();
            foreach (var cnt in contornos)
            {
                double area = Ops.AreaContorno(cnt);
                if (area < FraccionAreaMinima * areaImagen) continue;

                Ops.RectMinimo(cnt, out double cx, out double cy, out double w, out double h, out _);
                if (w == 0 || h == 0) continue;
                double aspecto = Math.Min(w, h) / Math.Max(w, h);
                if (aspecto <= AspectoMin || aspecto >= AspectoMax) continue;

                Rect bbox = Ops.BoundingRect(cnt);
                var interior = new Rect(bbox.X + bbox.W / 5, bbox.Y + bbox.H / 5,
                                        3 * bbox.W / 5, 3 * bbox.H / 5);
                if (interior.W <= 0 || interior.H <= 0) continue;
                double densidad = Ops.DensidadBordes(edges, interior);
                if (densidad < DensidadBordesMinima) continue;

                double dist = Math.Sqrt((cx - centroImg.X) * (cx - centroImg.X) +
                                        (cy - centroImg.Y) * (cy - centroImg.Y)) / diagonal;
                candidatos.Add(new Candidato { Contorno = cnt, Area = area, Dist = dist, Bbox = bbox });
            }

            if (candidatos.Count == 0) return null;

            double areaMayor = 0;
            foreach (var c in candidatos) if (c.Area > areaMayor) areaMayor = c.Area;

            Candidato elegido = null;
            foreach (var c in candidatos)
                if (c.Area >= FraccionAreaCompetencia * areaMayor)
                    if (elegido == null || c.Dist < elegido.Dist) elegido = c;

            Candidato contenedor = null;
            foreach (var c in candidatos)
                if (c != elegido && Contiene(c.Bbox, elegido.Bbox) && c.Area <= 8 * elegido.Area)
                    if (contenedor == null || c.Area < contenedor.Area) contenedor = c;
            if (contenedor != null) elegido = contenedor;

            return EsquinasPorLados(elegido.Contorno) ?? EsquinasPorRectangulo(elegido.Contorno);
        }

        static bool Contiene(Rect a, Rect b, double tol = 0.05)
        {
            double tx = tol * b.W, ty = tol * b.H;
            return a.X <= b.X + tx && a.Y <= b.Y + ty &&
                   a.X + a.W >= b.X + b.W - tx && a.Y + a.H >= b.Y + b.H - ty;
        }

        static double[] Interseccion(double[] l1, double[] l2)
        {
            double x1 = l1[0], y1 = l1[1], x2 = l1[2], y2 = l1[3];
            double x3 = l2[0], y3 = l2[1], x4 = l2[2], y4 = l2[3];
            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (denom == 0) return null;
            double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
            double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
            return new[] { px, py };
        }

        static PtF[] EsquinasPorLados(List<PtF> cnt)
        {
            double epsilon = 0.01 * Ops.ArcLength(cnt, true);
            List<PtF> approx = Ops.ApproxPolyDP(cnt, epsilon, true);
            if (approx.Count < 4) return null;

            var segmentos = new List<(double lon, double[] linea)>();
            for (int i = 0; i < approx.Count; i++)
            {
                PtF p1 = approx[i], p2 = approx[(i + 1) % approx.Count];
                double lon = Math.Sqrt((p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y));
                segmentos.Add((lon, new[] { p1.X, p1.Y, p2.X, p2.Y }));
            }
            segmentos.Sort((a, b) => b.lon.CompareTo(a.lon));
            int tope = Math.Min(4, segmentos.Count);

            var horizontales = new List<double[]>();
            var verticales = new List<double[]>();
            for (int i = 0; i < tope; i++)
            {
                double[] l = segmentos[i].linea;
                double ang = (Math.Atan2(l[3] - l[1], l[2] - l[0]) * 180.0 / Math.PI % 180.0 + 180.0) % 180.0;
                if (ang > 45 && ang < 135) verticales.Add(l); else horizontales.Add(l);
            }
            if (horizontales.Count != 2 || verticales.Count != 2) return null;

            horizontales.Sort((a, b) => ((a[1] + a[3]) / 2).CompareTo((b[1] + b[3]) / 2));
            verticales.Sort((a, b) => ((a[0] + a[2]) / 2).CompareTo((b[0] + b[2]) / 2));
            double[] top = horizontales[0], bottom = horizontales[1];
            double[] left = verticales[0], right = verticales[1];

            double[] tl = Interseccion(top, left), tr = Interseccion(top, right);
            double[] br = Interseccion(bottom, right), bl = Interseccion(bottom, left);
            if (tl == null || tr == null || br == null || bl == null) return null;
            return new[] { new PtF(tl[0], tl[1]), new PtF(tr[0], tr[1]),
                           new PtF(br[0], br[1]), new PtF(bl[0], bl[1]) };
        }

        static PtF[] EsquinasPorRectangulo(List<PtF> cnt)
        {
            Ops.RectMinimo(cnt, out _, out _, out _, out _, out PtF[] caja);
            PtF tl = caja[0], tr = caja[0], br = caja[0], bl = caja[0];
            double sMin = double.MaxValue, sMax = double.MinValue, dMin = double.MaxValue, dMax = double.MinValue;
            foreach (var p in caja)
            {
                double s = p.X + p.Y, d = p.Y - p.X;
                if (s < sMin) { sMin = s; tl = p; }
                if (s > sMax) { sMax = s; br = p; }
                if (d < dMin) { dMin = d; tr = p; }
                if (d > dMax) { dMax = d; bl = p; }
            }
            return new[] { tl, tr, br, bl };
        }

        static ImagenColor CorregirPerspectiva(ImagenColor img)
        {
            PtF[] esquinas = DetectarCartel(img);
            if (esquinas == null)
                return Ops.Redimensionar(img, AnchoSalida, AltoSalida);

            var destino = new[]
            {
                new PtF(0, 0), new PtF(AnchoSalida - 1, 0),
                new PtF(AnchoSalida - 1, AltoSalida - 1), new PtF(0, AltoSalida - 1)
            };
            // Homografia salida->entrada para muestrear directamente.
            double[] hInv = Ops.GetPerspectiveTransform(destino, esquinas);
            return Ops.WarpPerspective(img, hInv, AnchoSalida, AltoSalida);
        }

        // ---------------- Mejora fotometrica ----------------

        static ImagenColor AtenuarReflejos(ImagenColor img)
        {
            int n = img.Ancho * img.Alto;
            var saturados = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int b = img.Datos[i * 3], g = img.Datos[i * 3 + 1], r = img.Datos[i * 3 + 2];
                int max = Math.Max(b, Math.Max(g, r));
                int min = Math.Min(b, Math.Min(g, r));
                int s = max == 0 ? 0 : (int)Math.Round((max - min) * 255.0 / max);
                saturados[i] = (byte)((max > 245 && s < 40) ? 1 : 0);
            }

            Ops.EtiquetarComponentes(saturados, img.Ancho, img.Alto, out int[] etiquetas,
                                     out List<Rect> _, out List<int> areas, out List<PtF> __);
            double areaImagen = n;
            var mascara = new byte[n];
            bool hayMascara = false;
            for (int i = 0; i < n; i++)
            {
                int e = etiquetas[i];
                if (e > 0 && areas[e - 1] < 0.015 * areaImagen) { mascara[i] = 255; hayMascara = true; }
            }
            if (!hayMascara) { var copia = new ImagenColor(img.Ancho, img.Alto); Array.Copy(img.Datos, copia.Datos, img.Datos.Length); return copia; }

            Ops.DilatarBinInPlace(mascara, img.Ancho, img.Alto, 3);
            return Ops.InpaintDifusion(img, mascara, 40);
        }

        static ImagenGris AplanarIluminacion(ImagenGris gris)
        {
            ImagenGris fondo = Ops.CierreMorfologico(gris, 61);
            var plano = new ImagenGris(gris.Ancho, gris.Alto);
            for (int i = 0; i < gris.Datos.Length; i++)
            {
                double f = Math.Max((int)fondo.Datos[i], 1);
                double v = gris.Datos[i] / f * 230.0;
                plano.Datos[i] = (byte)(v > 255 ? 255 : (v < 0 ? 0 : v));
            }
            ImagenGris realzado = Ops.Clahe(plano, 2.0, 8);
            return Ops.FiltroMediana3(realzado);
        }

        static ImagenGris SuprimirLetraPequenaPeriferica(ImagenGris gris)
        {
            int alto = gris.Alto, ancho = gris.Ancho;
            int margenX = (int)(ancho * 0.15), margenY = (int)(alto * 0.15);
            int altoMaximoBorrable = (int)(alto * 0.04);

            byte[] bin = Ops.OtsuBinInv(gris);
            Ops.EtiquetarComponentes(bin, ancho, alto, out int[] etiquetas,
                                     out List<Rect> rects, out List<int> _, out List<PtF> centroides);

            var limpio = new ImagenGris(ancho, alto);
            Array.Copy(gris.Datos, limpio.Datos, gris.Datos.Length);
            int nComp = rects.Count;
            var borrar = new bool[nComp + 1];
            for (int c = 1; c <= nComp; c++)
            {
                double h = rects[c - 1].H;
                double cx = centroides[c - 1].X, cy = centroides[c - 1].Y;
                bool periferia = cx < margenX || cx > ancho - margenX || cy < margenY || cy > alto - margenY;
                if (periferia && h <= altoMaximoBorrable) borrar[c] = true;
            }
            for (int i = 0; i < etiquetas.Length; i++)
            {
                int e = etiquetas[i];
                if (e > 0 && borrar[e]) limpio.Datos[i] = 255;
            }
            return limpio;
        }

        static ImagenGris MejorarParaOcr(ImagenColor img)
        {
            ImagenColor sinReflejos = AtenuarReflejos(img);
            ImagenGris gris = Ops.AGris(sinReflejos);
            ImagenGris plano = AplanarIluminacion(gris);
            return SuprimirLetraPequenaPeriferica(plano);
        }
    }
}
