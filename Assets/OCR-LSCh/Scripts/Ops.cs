// Operaciones primitivas de imagen en C# puro (motor del preprocesamiento).
using System;
using System.Collections.Generic;

namespace OcrLsch.Vision
{
    static class Ops
    {
        // ---------------- Conversiones ----------------

        public static ImagenGris AGris(ImagenColor img)
        {
            var g = new ImagenGris(img.Ancho, img.Alto);
            for (int i = 0; i < img.Ancho * img.Alto; i++)
            {
                int b = img.Datos[i * 3], gr = img.Datos[i * 3 + 1], r = img.Datos[i * 3 + 2];
                g.Datos[i] = (byte)((r * 299 + gr * 587 + b * 114) / 1000);
            }
            return g;
        }

        // ---------------- Desenfoque gaussiano 5x5 (separable) ----------------

        public static ImagenGris GaussianBlur5(ImagenGris src)
        {
            int w = src.Ancho, h = src.Alto;
            int[] k = { 1, 4, 6, 4, 1 };
            var tmp = new int[w * h];
            var dst = new ImagenGris(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int acc = 0;
                    for (int t = -2; t <= 2; t++)
                    {
                        int xx = Clamp(x + t, 0, w - 1);
                        acc += src.Datos[y * w + xx] * k[t + 2];
                    }
                    tmp[y * w + x] = acc;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int acc = 0;
                    for (int t = -2; t <= 2; t++)
                    {
                        int yy = Clamp(y + t, 0, h - 1);
                        acc += tmp[yy * w + x] * k[t + 2];
                    }
                    dst.Datos[y * w + x] = (byte)(acc / 256);
                }
            return dst;
        }

        // ---------------- Canny ----------------

        public static ImagenGris Canny(ImagenGris src, int bajo, int alto)
        {
            int w = src.Ancho, h = src.Alto;
            var gx = new int[w * h];
            var gy = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int x0 = Clamp(x - 1, 0, w - 1), x1 = Clamp(x + 1, 0, w - 1);
                    int y0 = Clamp(y - 1, 0, h - 1), y1 = Clamp(y + 1, 0, h - 1);
                    byte p00 = src.Datos[y0 * w + x0], p01 = src.Datos[y0 * w + x], p02 = src.Datos[y0 * w + x1];
                    byte p10 = src.Datos[y * w + x0], p12 = src.Datos[y * w + x1];
                    byte p20 = src.Datos[y1 * w + x0], p21 = src.Datos[y1 * w + x], p22 = src.Datos[y1 * w + x1];
                    gx[y * w + x] = (p02 + 2 * p12 + p22) - (p00 + 2 * p10 + p20);
                    gy[y * w + x] = (p20 + 2 * p21 + p22) - (p00 + 2 * p01 + p02);
                }

            var mag = new int[w * h];
            for (int i = 0; i < w * h; i++) mag[i] = Math.Abs(gx[i]) + Math.Abs(gy[i]); // L1 (OpenCV por defecto)

            // Supresion de no-maximos segun direccion del gradiente.
            var nms = new int[w * h];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    int m = mag[i];
                    if (m == 0) continue;
                    double ang = Math.Atan2(gy[i], gx[i]) * 180.0 / Math.PI;
                    if (ang < 0) ang += 180;
                    int a, b;
                    if (ang < 22.5 || ang >= 157.5) { a = mag[i - 1]; b = mag[i + 1]; }
                    else if (ang < 67.5) { a = mag[i - w + 1]; b = mag[i + w - 1]; }
                    else if (ang < 112.5) { a = mag[i - w]; b = mag[i + w]; }
                    else { a = mag[i - w - 1]; b = mag[i + w + 1]; }
                    if (m >= a && m >= b) nms[i] = m;
                }

            // Doble umbral + histeresis por BFS desde bordes fuertes.
            var salida = new ImagenGris(w, h);
            var pila = new Stack<int>();
            for (int i = 0; i < w * h; i++)
                if (nms[i] >= alto) { salida.Datos[i] = 255; pila.Push(i); }
            while (pila.Count > 0)
            {
                int i = pila.Pop();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (salida.Datos[j] == 0 && nms[j] >= bajo) { salida.Datos[j] = 255; pila.Push(j); }
                    }
            }
            return salida;
        }

        // ---------------- Dilatacion binaria (cuadrado k x k, n iteraciones) ----------------

        public static ImagenGris Dilatar(ImagenGris src, int k, int iters)
        {
            int w = src.Ancho, h = src.Alto, r = k / 2;
            var cur = (byte[])src.Datos.Clone();
            for (int it = 0; it < iters; it++)
            {
                var next = new byte[w * h];
                // Separable: dilatacion horizontal luego vertical.
                var tmp = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        byte m = 0;
                        for (int t = -r; t <= r; t++)
                        {
                            int xx = x + t; if (xx < 0 || xx >= w) continue;
                            if (cur[y * w + xx] != 0) { m = 255; break; }
                        }
                        tmp[y * w + x] = m;
                    }
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        byte m = 0;
                        for (int t = -r; t <= r; t++)
                        {
                            int yy = y + t; if (yy < 0 || yy >= h) continue;
                            if (tmp[yy * w + x] != 0) { m = 255; break; }
                        }
                        next[y * w + x] = m;
                    }
                cur = next;
            }
            var dst = new ImagenGris(w, h); dst.Datos = cur; return dst;
        }

        public static void DilatarBinInPlace(byte[] mask, int w, int h, int k)
        {
            int r = k / 2;
            var tmp = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte m = 0;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (mask[ny * w + nx] != 0) { m = 255; }
                        }
                    tmp[y * w + x] = m;
                }
            Array.Copy(tmp, mask, mask.Length);
        }

        // ---------------- Contornos externos (trazado de Moore) ----------------

        public static List<List<PtF>> EncontrarContornosExternos(ImagenGris bin)
        {
            int w = bin.Ancho, h = bin.Alto;
            byte[] p = bin.Datos;
            var visitado = new bool[w * h];
            var contornos = new List<List<PtF>>();

            // Vecindario de Moore en orden horario empezando por el Este.
            int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (p[idx] == 0 || visitado[idx]) continue;

                    var contorno = TrazarBorde(p, w, h, x, y, dx, dy);
                    if (contorno.Count >= 3) contornos.Add(contorno);
                    RellenarComponente(p, w, h, x, y, visitado);
                }
            return contornos;
        }

        static List<PtF> TrazarBorde(byte[] p, int w, int h, int sx, int sy, int[] dx, int[] dy)
        {
            var contorno = new List<PtF>();
            int cx = sx, cy = sy;
            int dir = 6; // llegamos desde el Oeste -> empezar a mirar desde Norte-Oeste
            contorno.Add(new PtF(cx, cy));
            int maxPasos = 8 * (w + h) + 16;
            int primerNx = -1, primerNy = -1, pasos = 0;

            while (pasos++ < maxPasos)
            {
                bool encontrado = false;
                int inicio = (dir + 6) % 8; // retroceder para buscar en sentido horario
                for (int t = 0; t < 8; t++)
                {
                    int d = (inicio + t) % 8;
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (p[ny * w + nx] != 0)
                    {
                        cx = nx; cy = ny; dir = d; encontrado = true;
                        break;
                    }
                }
                if (!encontrado) break; // pixel aislado
                if (cx == sx && cy == sy)
                {
                    if (primerNx < 0) { primerNx = cx; primerNy = cy; }
                    else break; // volvimos al inicio
                    break;
                }
                contorno.Add(new PtF(cx, cy));
            }
            return contorno;
        }

        static void RellenarComponente(byte[] p, int w, int h, int sx, int sy, bool[] visitado)
        {
            var pila = new Stack<int>();
            int start = sy * w + sx;
            if (visitado[start]) return;
            pila.Push(start);
            visitado[start] = true;
            while (pila.Count > 0)
            {
                int i = pila.Pop();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (!visitado[j] && p[j] != 0) { visitado[j] = true; pila.Push(j); }
                    }
            }
        }

        // ---------------- Metricas de contorno ----------------

        public static double AreaContorno(List<PtF> c)
        {
            double a = 0;
            int n = c.Count;
            for (int i = 0; i < n; i++)
            {
                PtF p = c[i], q = c[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return Math.Abs(a) / 2.0;
        }

        public static double ArcLength(List<PtF> c, bool cerrado)
        {
            double L = 0;
            int n = c.Count;
            int lim = cerrado ? n : n - 1;
            for (int i = 0; i < lim; i++)
            {
                PtF p = c[i], q = c[(i + 1) % n];
                L += Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            }
            return L;
        }

        public static Rect BoundingRect(List<PtF> c)
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (var p in c)
            {
                if (p.X < minx) minx = p.X; if (p.X > maxx) maxx = p.X;
                if (p.Y < miny) miny = p.Y; if (p.Y > maxy) maxy = p.Y;
            }
            return new Rect((int)minx, (int)miny, (int)(maxx - minx) + 1, (int)(maxy - miny) + 1);
        }

        public static double DensidadBordes(ImagenGris edges, Rect r)
        {
            int w = edges.Ancho, h = edges.Alto;
            int x0 = Clamp(r.X, 0, w), y0 = Clamp(r.Y, 0, h);
            int x1 = Clamp(r.X + r.W, 0, w), y1 = Clamp(r.Y + r.H, 0, h);
            long area = (long)(x1 - x0) * (y1 - y0);
            if (area <= 0) return 0;
            long nz = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (edges.Datos[y * w + x] != 0) nz++;
            return (double)nz / area;
        }

        // ---------------- Envolvente convexa + rectangulo minimo ----------------

        static List<PtF> EnvolventeConvexa(List<PtF> pts)
        {
            var p = new List<PtF>(pts);
            p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            int n = p.Count;
            if (n < 3) return p;
            var hull = new PtF[2 * n];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cruz(hull[k - 2], hull[k - 1], p[i]) <= 0) k--;
                hull[k++] = p[i];
            }
            int lower = k + 1;
            for (int i = n - 2; i >= 0; i--)
            {
                while (k >= lower && Cruz(hull[k - 2], hull[k - 1], p[i]) <= 0) k--;
                hull[k++] = p[i];
            }
            var res = new List<PtF>();
            for (int i = 0; i < k - 1; i++) res.Add(hull[i]);
            return res;
        }

        static double Cruz(PtF o, PtF a, PtF b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        public static void RectMinimo(List<PtF> cnt, out double cx, out double cy,
                                      out double w, out double h, out PtF[] esquinas)
        {
            List<PtF> hull = EnvolventeConvexa(cnt);
            cx = cy = 0; w = h = 0; esquinas = new PtF[4];
            if (hull.Count < 2)
            {
                if (hull.Count == 1) { cx = hull[0].X; cy = hull[0].Y; }
                return;
            }
            double mejorArea = double.MaxValue;
            int m = hull.Count;
            for (int i = 0; i < m; i++)
            {
                PtF a = hull[i], b = hull[(i + 1) % m];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double len = Math.Sqrt(ex * ex + ey * ey);
                if (len < 1e-9) continue;
                ex /= len; ey /= len;
                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (var p in hull)
                {
                    double u = p.X * ex + p.Y * ey;
                    double v = -p.X * ey + p.Y * ex;
                    if (u < minU) minU = u; if (u > maxU) maxU = u;
                    if (v < minV) minV = v; if (v > maxV) maxV = v;
                }
                double du = maxU - minU, dv = maxV - minV;
                double area = du * dv;
                if (area < mejorArea)
                {
                    mejorArea = area; w = du; h = dv;
                    double u0 = (minU + maxU) / 2, v0 = (minV + maxV) / 2;
                    cx = u0 * ex - v0 * ey;
                    cy = u0 * ey + v0 * ex;
                    double[] us = { minU, maxU, maxU, minU };
                    double[] vs = { minV, minV, maxV, maxV };
                    for (int c = 0; c < 4; c++)
                        esquinas[c] = new PtF(us[c] * ex - vs[c] * ey, us[c] * ey + vs[c] * ex);
                }
            }
        }

        // ---------------- Douglas-Peucker (cerrado) ----------------

        public static List<PtF> ApproxPolyDP(List<PtF> pts, double eps, bool cerrado)
        {
            int n = pts.Count;
            if (n < 3) return new List<PtF>(pts);
            // Punto mas lejano del primero, y el mas lejano de ese: extremos.
            int i0 = 0; double dmax = -1;
            for (int i = 1; i < n; i++) { double d = Dist2(pts[0], pts[i]); if (d > dmax) { dmax = d; i0 = i; } }
            int i1 = 0; dmax = -1;
            for (int i = 0; i < n; i++) { double d = Dist2(pts[i0], pts[i]); if (d > dmax) { dmax = d; i1 = i; } }

            int lo = Math.Min(i0, i1), hi = Math.Max(i0, i1);
            var arco1 = new List<PtF>();
            for (int i = lo; i <= hi; i++) arco1.Add(pts[i]);
            var arco2 = new List<PtF>();
            for (int i = hi; i < n; i++) arco2.Add(pts[i]);
            for (int i = 0; i <= lo; i++) arco2.Add(pts[i]);

            var r1 = new List<PtF>(); DP(arco1, 0, arco1.Count - 1, eps, r1);
            var r2 = new List<PtF>(); DP(arco2, 0, arco2.Count - 1, eps, r2);

            var res = new List<PtF>();
            foreach (var p in r1) res.Add(p);
            for (int i = 1; i < r2.Count - 1; i++) res.Add(r2[i]);
            return res;
        }

        static void DP(List<PtF> pts, int i, int j, double eps, List<PtF> outp)
        {
            double dmax = 0; int idx = -1;
            for (int k = i + 1; k < j; k++)
            {
                double d = DistPuntoSegmento(pts[k], pts[i], pts[j]);
                if (d > dmax) { dmax = d; idx = k; }
            }
            if (dmax > eps && idx >= 0)
            {
                var izq = new List<PtF>(); DP(pts, i, idx, eps, izq);
                var der = new List<PtF>(); DP(pts, idx, j, eps, der);
                for (int k = 0; k < izq.Count - 1; k++) outp.Add(izq[k]);
                foreach (var p in der) outp.Add(p);
            }
            else { outp.Add(pts[i]); outp.Add(pts[j]); }
        }

        static double Dist2(PtF a, PtF b) { double dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }

        static double DistPuntoSegmento(PtF p, PtF a, PtF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return Math.Sqrt(Dist2(p, a));
            double dist = Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / len;
            return dist;
        }

        // ---------------- Homografia + warp ----------------

        public static double[] GetPerspectiveTransform(PtF[] src, PtF[] dst)
        {
            // Resuelve A h = b (8x8) para h = [a,b,c,d,e,f,g,h], i=1.
            var A = new double[8, 8];
            var bb = new double[8];
            for (int i = 0; i < 4; i++)
            {
                double x = src[i].X, y = src[i].Y, X = dst[i].X, Y = dst[i].Y;
                A[i * 2, 0] = x; A[i * 2, 1] = y; A[i * 2, 2] = 1;
                A[i * 2, 6] = -x * X; A[i * 2, 7] = -y * X; bb[i * 2] = X;
                A[i * 2 + 1, 3] = x; A[i * 2 + 1, 4] = y; A[i * 2 + 1, 5] = 1;
                A[i * 2 + 1, 6] = -x * Y; A[i * 2 + 1, 7] = -y * Y; bb[i * 2 + 1] = Y;
            }
            double[] h = ResolverGauss(A, bb, 8);
            return new[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 };
        }

        static double[] ResolverGauss(double[,] A, double[] b, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int piv = col; double max = Math.Abs(A[col, col]);
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(A[r, col]) > max) { max = Math.Abs(A[r, col]); piv = r; }
                if (piv != col)
                {
                    for (int c = 0; c < n; c++) { double t = A[col, c]; A[col, c] = A[piv, c]; A[piv, c] = t; }
                    double tb = b[col]; b[col] = b[piv]; b[piv] = tb;
                }
                double d = A[col, col];
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = A[r, col] / d;
                    if (f == 0) continue;
                    for (int c = col; c < n; c++) A[r, c] -= f * A[col, c];
                    b[r] -= f * b[col];
                }
            }
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = b[i] / A[i, i];
            return x;
        }

        public static ImagenColor WarpPerspective(ImagenColor src, double[] hSalidaAEntrada, int wOut, int hOut)
        {
            var dst = new ImagenColor(wOut, hOut);
            double[] H = hSalidaAEntrada;
            for (int y = 0; y < hOut; y++)
                for (int x = 0; x < wOut; x++)
                {
                    double den = H[6] * x + H[7] * y + H[8];
                    double sx = (H[0] * x + H[1] * y + H[2]) / den;
                    double sy = (H[3] * x + H[4] * y + H[5]) / den;
                    MuestrearBilineal(src, sx, sy, dst, (y * wOut + x) * 3);
                }
            return dst;
        }

        static void MuestrearBilineal(ImagenColor src, double sx, double sy, ImagenColor dst, int di)
        {
            int w = src.Ancho, h = src.Alto;
            if (sx < 0 || sy < 0 || sx > w - 1 || sy > h - 1)
            {
                dst.Datos[di] = dst.Datos[di + 1] = dst.Datos[di + 2] = 0; return;
            }
            int x0 = (int)sx, y0 = (int)sy;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            double fx = sx - x0, fy = sy - y0;
            for (int c = 0; c < 3; c++)
            {
                double v00 = src.Datos[(y0 * w + x0) * 3 + c];
                double v01 = src.Datos[(y0 * w + x1) * 3 + c];
                double v10 = src.Datos[(y1 * w + x0) * 3 + c];
                double v11 = src.Datos[(y1 * w + x1) * 3 + c];
                double v = v00 * (1 - fx) * (1 - fy) + v01 * fx * (1 - fy) +
                           v10 * (1 - fx) * fy + v11 * fx * fy;
                dst.Datos[di + c] = (byte)(v + 0.5);
            }
        }

        public static ImagenColor Redimensionar(ImagenColor src, int wOut, int hOut)
        {
            var dst = new ImagenColor(wOut, hOut);
            double ex = (double)src.Ancho / wOut, ey = (double)src.Alto / hOut;
            for (int y = 0; y < hOut; y++)
                for (int x = 0; x < wOut; x++)
                    MuestrearBilineal(src, x * ex, y * ey, dst, (y * wOut + x) * 3);
            return dst;
        }

        public static ImagenGris RedimensionarGris(ImagenGris src, int wOut, int hOut)
        {
            var dst = new ImagenGris(wOut, hOut);
            int w = src.Ancho, h = src.Alto;
            double ex = (double)w / wOut, ey = (double)h / hOut;
            for (int y = 0; y < hOut; y++)
                for (int x = 0; x < wOut; x++)
                {
                    double sx = x * ex, sy = y * ey;
                    int x0 = (int)sx, y0 = (int)sy;
                    int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
                    double fx = sx - x0, fy = sy - y0;
                    double v00 = src.Datos[y0 * w + x0], v01 = src.Datos[y0 * w + x1];
                    double v10 = src.Datos[y1 * w + x0], v11 = src.Datos[y1 * w + x1];
                    double v = v00 * (1 - fx) * (1 - fy) + v01 * fx * (1 - fy) +
                               v10 * (1 - fx) * fy + v11 * fx * fy;
                    dst.Datos[y * wOut + x] = (byte)(v + 0.5);
                }
            return dst;
        }

        public static ImagenGris RecortarGris(ImagenGris src, int x0, int y0, int w, int h)
        {
            var dst = new ImagenGris(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = Clamp(x0 + x, 0, src.Ancho - 1), sy = Clamp(y0 + y, 0, src.Alto - 1);
                    dst.Datos[y * w + x] = src.Datos[sy * src.Ancho + sx];
                }
            return dst;
        }

        // ---------------- Componentes conexos (8-conn) con estadisticas ----------------

        public static void EtiquetarComponentes(byte[] bin, int w, int h, out int[] etiquetas,
            out List<Rect> rects, out List<int> areas, out List<PtF> centroides)
        {
            etiquetas = new int[w * h];
            rects = new List<Rect>();
            areas = new List<int>();
            centroides = new List<PtF>();
            int actual = 0;
            var pila = new Stack<int>();
            for (int s = 0; s < w * h; s++)
            {
                if (bin[s] == 0 || etiquetas[s] != 0) continue;
                actual++;
                int minx = w, miny = h, maxx = 0, maxy = 0, area = 0;
                double sumx = 0, sumy = 0;
                pila.Push(s); etiquetas[s] = actual;
                while (pila.Count > 0)
                {
                    int i = pila.Pop();
                    int x = i % w, y = i / w;
                    area++; sumx += x; sumy += y;
                    if (x < minx) minx = x; if (x > maxx) maxx = x;
                    if (y < miny) miny = y; if (y > maxy) maxy = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (bin[j] != 0 && etiquetas[j] == 0) { etiquetas[j] = actual; pila.Push(j); }
                        }
                }
                rects.Add(new Rect(minx, miny, maxx - minx + 1, maxy - miny + 1));
                areas.Add(area);
                centroides.Add(new PtF(sumx / area, sumy / area));
            }
        }

        // Variante que expone estadisticas como arreglos simples (sin tipos
        // internos), pensada para el motor OCR. Devuelve el numero de
        // componentes; los arreglos estan indexados por etiqueta (1..n).
        public static int EtiquetarStats(byte[] bin, int w, int h, out int[] etiquetas,
            out int[] left, out int[] top, out int[] ancho, out int[] alto, out int[] area)
        {
            EtiquetarComponentes(bin, w, h, out etiquetas, out List<Rect> rects,
                                 out List<int> areas, out List<PtF> _);
            int n = rects.Count;
            left = new int[n + 1]; top = new int[n + 1];
            ancho = new int[n + 1]; alto = new int[n + 1]; area = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                left[i + 1] = rects[i].X; top[i + 1] = rects[i].Y;
                ancho[i + 1] = rects[i].W; alto[i + 1] = rects[i].H; area[i + 1] = areas[i];
            }
            return n;
        }

        // ---------------- Inpaint por difusion (reflejos pequenos) ----------------

        public static ImagenColor InpaintDifusion(ImagenColor img, byte[] mask, int iters)
        {
            int w = img.Ancho, h = img.Alto;
            var res = new ImagenColor(w, h);
            Array.Copy(img.Datos, res.Datos, img.Datos.Length);
            for (int it = 0; it < iters; it++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (mask[i] == 0) continue;
                        int cuenta = 0; int[] suma = new int[3];
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                                int j = ny * w + nx;
                                suma[0] += res.Datos[j * 3]; suma[1] += res.Datos[j * 3 + 1]; suma[2] += res.Datos[j * 3 + 2];
                                cuenta++;
                            }
                        if (cuenta > 0)
                            for (int c = 0; c < 3; c++) res.Datos[i * 3 + c] = (byte)(suma[c] / cuenta);
                    }
            return res;
        }

        // ---------------- Cierre morfologico (rectangulo grande, separable) ----------------

        public static ImagenGris CierreMorfologico(ImagenGris src, int k)
        {
            ImagenGris dil = DilatarGrisSeparable(src, k, true);
            return DilatarGrisSeparable(dil, k, false);
        }

        static ImagenGris DilatarGrisSeparable(ImagenGris src, int k, bool dilatar)
        {
            int w = src.Ancho, h = src.Alto, r = k / 2;
            var tmp = new byte[w * h];
            var dst = new ImagenGris(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int val = dilatar ? 0 : 255;
                    for (int t = -r; t <= r; t++)
                    {
                        int xx = Clamp(x + t, 0, w - 1);
                        byte v = src.Datos[y * w + xx];
                        val = dilatar ? Math.Max(val, v) : Math.Min(val, v);
                    }
                    tmp[y * w + x] = (byte)val;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int val = dilatar ? 0 : 255;
                    for (int t = -r; t <= r; t++)
                    {
                        int yy = Clamp(y + t, 0, h - 1);
                        byte v = tmp[yy * w + x];
                        val = dilatar ? Math.Max(val, v) : Math.Min(val, v);
                    }
                    dst.Datos[y * w + x] = (byte)val;
                }
            return dst;
        }

        // ---------------- CLAHE ----------------

        public static ImagenGris Clahe(ImagenGris src, double clip, int tiles)
        {
            int w = src.Ancho, h = src.Alto;
            int tw = (w + tiles - 1) / tiles, th = (h + tiles - 1) / tiles;
            var lut = new byte[tiles, tiles, 256];
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    int x0 = tx * tw, y0 = ty * th;
                    int x1 = Math.Min(x0 + tw, w), y1 = Math.Min(y0 + th, h);
                    var hist = new int[256];
                    int cuenta = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) { hist[src.Datos[y * w + x]]++; cuenta++; }
                    if (cuenta == 0) { for (int i = 0; i < 256; i++) lut[ty, tx, i] = (byte)i; continue; }
                    int limite = (int)Math.Max(1, clip * cuenta / 256.0);
                    int exceso = 0;
                    for (int i = 0; i < 256; i++) if (hist[i] > limite) { exceso += hist[i] - limite; hist[i] = limite; }
                    int repartir = exceso / 256;
                    for (int i = 0; i < 256; i++) hist[i] += repartir;
                    int acum = 0;
                    for (int i = 0; i < 256; i++) { acum += hist[i]; lut[ty, tx, i] = (byte)(255.0 * acum / cuenta); }
                }

            var dst = new ImagenGris(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double gx = (x - tw / 2.0) / tw;
                    double gy = (y - th / 2.0) / th;
                    int tx0 = (int)Math.Floor(gx), ty0 = (int)Math.Floor(gy);
                    double fx = gx - tx0, fy = gy - ty0;
                    int txa = Clamp(tx0, 0, tiles - 1), txb = Clamp(tx0 + 1, 0, tiles - 1);
                    int tya = Clamp(ty0, 0, tiles - 1), tyb = Clamp(ty0 + 1, 0, tiles - 1);
                    byte v = src.Datos[y * w + x];
                    double v00 = lut[tya, txa, v], v01 = lut[tya, txb, v];
                    double v10 = lut[tyb, txa, v], v11 = lut[tyb, txb, v];
                    double val = v00 * (1 - fx) * (1 - fy) + v01 * fx * (1 - fy) +
                                 v10 * (1 - fx) * fy + v11 * fx * fy;
                    dst.Datos[y * w + x] = (byte)(val + 0.5);
                }
            return dst;
        }

        // ---------------- Filtro de mediana 3x3 ----------------

        public static ImagenGris FiltroMediana3(ImagenGris src)
        {
            int w = src.Ancho, h = src.Alto;
            var dst = new ImagenGris(w, h);
            var v = new byte[9];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = Clamp(x + dx, 0, w - 1), ny = Clamp(y + dy, 0, h - 1);
                            v[n++] = src.Datos[ny * w + nx];
                        }
                    Array.Sort(v);
                    dst.Datos[y * w + x] = v[4];
                }
            return dst;
        }

        // ---------------- Otsu (binario invertido) ----------------

        public static byte[] OtsuBinInv(ImagenGris src)
        {
            int w = src.Ancho, h = src.Alto, n = w * h;
            var hist = new int[256];
            for (int i = 0; i < n; i++) hist[src.Datos[i]]++;
            double sum = 0; for (int i = 0; i < 256; i++) sum += i * hist[i];
            double sumB = 0; int wB = 0; double maxVar = -1; int umbral = 0;
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t]; if (wB == 0) continue;
                int wF = n - wB; if (wF == 0) break;
                sumB += t * hist[t];
                double mB = sumB / wB, mF = (sum - sumB) / wF;
                double var = (double)wB * wF * (mB - mF) * (mB - mF);
                if (var > maxVar) { maxVar = var; umbral = t; }
            }
            var bin = new byte[n];
            for (int i = 0; i < n; i++) bin[i] = (byte)(src.Datos[i] < umbral ? 255 : 0); // INV
            return bin;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
