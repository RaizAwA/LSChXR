// Pre/post-procesamiento de PaddleOCR (PP-OCRv4) en C# PURO y testeable
// (sin Unity ni Sentis). Portado fielmente de rapidocr-onnxruntime:
//   - Detector DB: normalización + extracción de cajas (umbral, score, unclip).
//   - Reconocedor CRNN/CTC: resize_norm_img + decodificación CTC greedy.
//
// MotorOcr.cs usa estas funciones alrededor de la inferencia con Sentis.
// Al ser puro, este archivo se valida en PC contra la referencia Python.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OcrLsch.Vision;

namespace OcrLsch.Vision
{
    public struct CajaTexto { public int X, Y, W, H; }

    /// <summary>Una detección de texto del OCR, con su caja y confianza.</summary>
    public struct DeteccionOcr
    {
        public string Texto;
        public float Confianza;
        public float X, Derecha, Arriba, Abajo;
        public float Alto => Abajo - Arriba;
        public float CentroX => (X + Derecha) / 2f;
        public float CentroY => (Arriba + Abajo) / 2f;
    }

    public static class OcrPaddle
    {
        // ---- Parámetros de PaddleOCR / RapidOCR (valores por defecto) ----
        public const int RecAlto = 48;         // altura de entrada del reconocedor
        public const int RecAnchoBase = 320;   // ancho base (imgW en rec_img_shape)
        public const float DetThresh = 0.3f;   // binarización del mapa DB
        public const float DetBoxThresh = 0.5f;// score medio mínimo por caja
        public const float DetUnclip = 1.6f;   // expansión de la caja (unclip_ratio)

        // Normalización compartida por detector y reconocedor: (x/255-0.5)/0.5.
        static float Norm(byte v) => (v / 255f - 0.5f) / 0.5f;

        // ------------------------------------------------------------------
        // Diccionario CTC
        // ------------------------------------------------------------------

        /// <summary>Construye la lista de caracteres indexada como la salida
        /// del modelo: índice 0 = blank, 1..N = diccionario base, N+1 = espacio.
        /// `dictBase` son las líneas de charset_paddle.txt.</summary>
        public static string[] ConstruirCharset(string[] dictBase)
        {
            var lista = new string[dictBase.Length + 2];
            lista[0] = "";                       // blank (se ignora al decodificar)
            for (int i = 0; i < dictBase.Length; i++) lista[i + 1] = dictBase[i];
            lista[dictBase.Length + 1] = " ";    // espacio
            return lista;
        }

        // ------------------------------------------------------------------
        // Reconocedor: preprocesamiento (resize_norm_img)
        // ------------------------------------------------------------------

        /// <summary>Prepara el tensor de entrada del reconocedor para un
        /// recorte en gris. Devuelve el tensor [3,48,anchoTensor] (float NCHW
        /// sin la dimensión N) y el ancho útil real usado (resizedW).</summary>
        public static float[] PreprocesarRec(ImagenGris recorte, out int anchoTensor, out int resizedW)
        {
            int h = recorte.Alto, w = recorte.Ancho;
            double whRatio = (double)w / h;
            double maxWhRatio = Math.Max((double)RecAnchoBase / RecAlto, whRatio);
            anchoTensor = (int)(RecAlto * maxWhRatio);

            double ratio = (double)w / h;
            resizedW = (Math.Ceiling(RecAlto * ratio) > anchoTensor)
                ? anchoTensor
                : (int)Math.Ceiling(RecAlto * ratio);
            if (resizedW < 1) resizedW = 1;

            ImagenGris redim = Ops.RedimensionarGris(recorte, resizedW, RecAlto);

            // Tensor 3x48xanchoTensor, canales replicados (gris), con relleno 0.
            var tensor = new float[3 * RecAlto * anchoTensor];
            int plano = RecAlto * anchoTensor;
            for (int y = 0; y < RecAlto; y++)
                for (int x = 0; x < resizedW; x++)
                {
                    float v = Norm(redim.Datos[y * resizedW + x]);
                    int idx = y * anchoTensor + x;
                    tensor[idx] = v;                 // canal 0
                    tensor[plano + idx] = v;         // canal 1
                    tensor[2 * plano + idx] = v;     // canal 2
                }
            return tensor;
        }

        // ------------------------------------------------------------------
        // Reconocedor: decodificación CTC greedy (CTCLabelDecode)
        // ------------------------------------------------------------------

        /// <summary>Decodifica la salida del reconocedor (probs [T,clases],
        /// fila-mayor) a texto y confianza media. `charset` indexado como el
        /// modelo (0 = blank). Igual que RapidOCR: argmax por paso, se quitan
        /// duplicados consecutivos y el blank (índice 0).</summary>
        public static (string texto, float conf) DecodificarCTC(float[] probs, int pasos, int clases, string[] charset)
        {
            var sb = new StringBuilder();
            double sumaConf = 0; int cuenta = 0; int anterior = -1;
            for (int t = 0; t < pasos; t++)
            {
                int mejor = 0; float mejorP = float.MinValue;
                int baseIdx = t * clases;
                for (int c = 0; c < clases; c++)
                {
                    float p = probs[baseIdx + c];
                    if (p > mejorP) { mejorP = p; mejor = c; }
                }
                bool duplicado = mejor == anterior;
                anterior = mejor;
                if (mejor == 0 || duplicado) continue; // blank o repetido
                if (mejor < charset.Length) sb.Append(charset[mejor]);
                sumaConf += mejorP; cuenta++;
            }
            float conf = cuenta > 0 ? (float)(sumaConf / cuenta) : 0f;
            return (sb.ToString(), conf);
        }

        // ------------------------------------------------------------------
        // Detector DB: normalización de entrada
        // ------------------------------------------------------------------

        /// <summary>Normaliza una imagen gris (replicada a 3 canales) al tensor
        /// de entrada del detector DB [3,H,W] (NCHW sin N).</summary>
        public static float[] PreprocesarDet(ImagenGris img)
        {
            int w = img.Ancho, h = img.Alto, plano = w * h;
            var tensor = new float[3 * plano];
            for (int i = 0; i < plano; i++)
            {
                float v = Norm(img.Datos[i]);
                tensor[i] = v; tensor[plano + i] = v; tensor[2 * plano + i] = v;
            }
            return tensor;
        }

        // ------------------------------------------------------------------
        // Detector DB: extracción de cajas desde el mapa de probabilidad
        // ------------------------------------------------------------------

        /// <summary>Extrae cajas de texto del mapa de probabilidad DB (valores
        /// 0..1, tamaño mapaW x mapaH). Umbraliza (DetThresh), toma componentes
        /// conexos, filtra por score medio (DetBoxThresh) y los expande
        /// (unclip). Las coordenadas se escalan del mapa a la imagen original
        /// (factores escalaX/escalaY).</summary>
        public static List<CajaTexto> ExtraerCajasDB(float[] mapa, int mapaW, int mapaH,
            double escalaX, double escalaY, int anchoImg, int altoImg)
        {
            var bin = new byte[mapaW * mapaH];
            for (int i = 0; i < bin.Length; i++) bin[i] = (byte)(mapa[i] > DetThresh ? 255 : 0);

            int n = Ops.EtiquetarStats(bin, mapaW, mapaH, out int[] etiquetas,
                out int[] left, out int[] top, out int[] ancho, out int[] alto, out int[] area);

            // Score medio por componente.
            var suma = new double[n + 1];
            for (int i = 0; i < mapa.Length; i++)
            {
                int e = etiquetas[i];
                if (e > 0) suma[e] += mapa[i];
            }

            var cajas = new List<CajaTexto>();
            for (int i = 1; i <= n; i++)
            {
                if (area[i] < 3) continue;
                double score = suma[i] / area[i];
                if (score < DetBoxThresh) continue;

                // Unclip: se expande la caja por una distancia proporcional al
                // área/perímetro (aprox. del offset de polígono de PaddleOCR).
                double bw = ancho[i], bh = alto[i];
                double perim = 2 * (bw + bh);
                double dist = perim > 0 ? (bw * bh) * DetUnclip / perim : 0;
                double x0 = (left[i] - dist) * escalaX;
                double y0 = (top[i] - dist) * escalaY;
                double x1 = (left[i] + bw + dist) * escalaX;
                double y1 = (top[i] + bh + dist) * escalaY;

                int ix0 = (int)Math.Max(0, Math.Round(x0));
                int iy0 = (int)Math.Max(0, Math.Round(y0));
                int ix1 = (int)Math.Min(anchoImg, Math.Round(x1));
                int iy1 = (int)Math.Min(altoImg, Math.Round(y1));
                if (ix1 - ix0 < 4 || iy1 - iy0 < 4) continue;
                cajas.Add(new CajaTexto { X = ix0, Y = iy0, W = ix1 - ix0, H = iy1 - iy0 });
            }
            return cajas;
        }

        // ------------------------------------------------------------------
        // Filtrado y ordenado de detecciones (versión C#, con clúster espacial)
        // ------------------------------------------------------------------

        const float ConfianzaAlta = 0.35f;
        const float ConfianzaMinima = 0.10f;
        const float AlturaRelativaMinima = 0.50f;
        const float AlturaRelativaBajaConfianza = 0.60f;
        // Dos cajas se agrupan si su separación es menor que este múltiplo de
        // la altura mediana de línea (idea 1: separar el cartel del afiche/logo).
        const float FactorClusterProximidad = 1.5f;

        /// <summary>Filtra las detecciones y arma el texto final. Respecto a la
        /// referencia Python, añade un paso de CLÚSTER ESPACIAL: agrupa las
        /// cajas por proximidad y conserva solo el grupo más cercano al centro
        /// del recorte, descartando texto de carteles/afiches/logos vecinos que
        /// hayan quedado dentro del recorte (p. ej. la imagen 4).</summary>
        public static string FiltrarYOrdenar(List<DeteccionOcr> detecciones,
                                             int anchoImg = 500, int altoImg = 700)
        {
            var cajas = detecciones.Where(d => d.Confianza >= ConfianzaMinima).ToList();
            if (cajas.Count == 0) return "";

            // (idea 1) Clúster espacial: conservar el grupo más central.
            cajas = ClusterCentral(cajas, anchoImg, altoImg);
            if (cajas.Count == 0) return "";

            var confiables = cajas.Where(c => c.Confianza >= ConfianzaAlta).ToList();
            float altoReferencia = (confiables.Count > 0 ? confiables : cajas).Max(c => c.Alto);

            cajas = cajas.Where(c =>
                (c.Confianza >= ConfianzaAlta && c.Alto >= AlturaRelativaMinima * altoReferencia) ||
                (c.Confianza < ConfianzaAlta && c.Alto >= AlturaRelativaBajaConfianza * altoReferencia)
            ).ToList();
            if (cajas.Count == 0) return "";

            cajas = cajas.OrderBy(c => c.Arriba).ToList();
            var lineas = new List<(List<DeteccionOcr> cajas, float arriba, float abajo)>();
            foreach (var caja in cajas)
            {
                if (lineas.Count > 0)
                {
                    var actual = lineas[lineas.Count - 1];
                    float centro = (caja.Arriba + caja.Abajo) / 2f;
                    if (actual.arriba <= centro && centro <= actual.abajo)
                    {
                        actual.cajas.Add(caja);
                        lineas[lineas.Count - 1] = (actual.cajas,
                            Math.Min(actual.arriba, caja.Arriba),
                            Math.Max(actual.abajo, caja.Abajo));
                        continue;
                    }
                }
                lineas.Add((new List<DeteccionOcr> { caja }, caja.Arriba, caja.Abajo));
            }

            float altoTipico = lineas.Average(l => l.abajo - l.arriba);
            var bloques = new List<List<string>>();
            var bloque = new List<string>();
            float abajoPrevio = float.NaN;
            foreach (var linea in lineas)
            {
                if (!float.IsNaN(abajoPrevio) && linea.arriba - abajoPrevio > 0.9f * altoTipico)
                {
                    bloques.Add(bloque);
                    bloque = new List<string>();
                }
                bloque.Add(string.Join(" ", linea.cajas.OrderBy(c => c.X).Select(c => c.Texto)));
                abajoPrevio = linea.abajo;
            }
            bloques.Add(bloque);

            return string.Join(". ", bloques
                .Where(b => b.Count > 0)
                .Select(b => string.Join(" ", b).TrimEnd('.')));
        }

        static List<DeteccionOcr> ClusterCentral(List<DeteccionOcr> cajas, int anchoImg, int altoImg)
        {
            int n = cajas.Count;
            if (n <= 1) return cajas;

            var alturas = cajas.Select(c => c.Alto).OrderBy(a => a).ToList();
            float mediana = alturas[alturas.Count / 2];
            float umbral = FactorClusterProximidad * mediana;

            // Union-find: unir cajas cuya separación (dx y dy) sea <= umbral.
            var padre = new int[n];
            for (int i = 0; i < n; i++) padre[i] = i;
            int Find(int a) { while (padre[a] != a) { padre[a] = padre[padre[a]]; a = padre[a]; } return a; }
            void Union(int a, int b) { padre[Find(a)] = Find(b); }
            // Se unen dos cajas si se SOLAPAN horizontalmente y están cercanas
            // en vertical. Exigir solape horizontal (no mera cercanía) separa
            // el cartel —cuyas líneas van centradas y se solapan entre sí— del
            // texto de un afiche/cartel vecino ubicado a un costado.
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float solapeX = Math.Min(cajas[i].Derecha, cajas[j].Derecha) - Math.Max(cajas[i].X, cajas[j].X);
                    float dy = Math.Max(0, Math.Max(cajas[i].Arriba, cajas[j].Arriba) - Math.Min(cajas[i].Abajo, cajas[j].Abajo));
                    if (solapeX > 0 && dy <= umbral) Union(i, j);
                }

            var grupos = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!grupos.ContainsKey(r)) grupos[r] = new List<int>();
                grupos[r].Add(i);
            }
            if (grupos.Count == 1) return cajas;

            // Se conserva el grupo con mayor ÁREA TOTAL de texto: el mensaje
            // principal del cartel domina el recorte, mientras que logos,
            // seriales y el texto de afiches/carteles vecinos que se colaron
            // son más pequeños. (El centro no sirve: el texto del cartel suele
            // ir debajo del pictograma, descentrado hacia abajo.)
            double AreaTexto(List<int> g) =>
                g.Sum(i => (double)(cajas[i].Derecha - cajas[i].X) * cajas[i].Alto);
            var elegido = grupos.Values.OrderByDescending(AreaTexto).First();
            return elegido.Select(i => cajas[i]).ToList();
        }
    }
}
