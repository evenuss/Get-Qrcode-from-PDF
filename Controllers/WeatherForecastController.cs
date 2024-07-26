using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using SautinSoft;

namespace QrCodeInPdfApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly ILogger<PdfController> _logger;

        public PdfController(ILogger<PdfController> logger)
        {
            _logger = logger;
        }

        [HttpPost("detect")]
        public async Task<IActionResult> Detect(IFormFile pdfFile)
        {
            if (pdfFile == null || pdfFile.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            string tempPdfFilePath = "";
            string outputImageFilePath = "";

            try
            {
                // Generate a unique temporary file name
                tempPdfFilePath = Path.Combine("Temp", Path.GetRandomFileName() + ".pdf");
                //tempPdfFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
                outputImageFilePath = Path.ChangeExtension(tempPdfFilePath, ".jpg");

                // Save the uploaded PDF to a temporary location
                await using (var stream = new FileStream(tempPdfFilePath, FileMode.Create))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                // Initialize PdfFocus
                PdfFocus pdfFocus = new();
                pdfFocus.ImageOptions.ImageFormat = PdfFocus.CImageOptions.ImageFormats.Jpeg;
                pdfFocus.ImageOptions.Dpi = 200;

                // Open the PDF file from the temporary location
                pdfFocus.OpenPdf(tempPdfFilePath);

                // Convert to Image
                int result = pdfFocus.ToImage(outputImageFilePath, 1);

                if (result != 0)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "PDF to image conversion failed.");
                }
                pdfFocus.ClosePdf();
                // Read the converted image
                byte[] imageBytes;
                using (var imageStream = new FileStream(outputImageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    imageBytes = new byte[imageStream.Length];
                    await imageStream.ReadAsync(imageBytes.AsMemory(0, (int)imageStream.Length));
                }

                Mat src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

                // Detect QR code
                QRCodeDetector qrDecoder = new();
                string decodedText = qrDecoder.DetectAndDecode(src, out Point2f[] points);

                if (string.IsNullOrEmpty(decodedText) || points.Length == 0)
                {
                    return NotFound("No QR code detected.");
                }

                // Mark the detected QR code on the image
                foreach (var point in points)
                {
                    Cv2.Circle(src, (int)point.X, (int)point.Y, 5, Scalar.Red, 2);
                }

                // Convert the marked image back to a byte array
                byte[] markedImage;
                using (var markedImageStream = new MemoryStream())
                {
                    Cv2.ImEncode(".jpg", src, out markedImage);
                }

                // Fetch the XML response from the URL and convert to JSON
                ResValidateFakturPm jsonResponse = new();
                try
                {
                    // Fetch the XML response from the URL
                    using (var httpClient = new System.Net.Http.HttpClient())
                    {
                        var response = await httpClient.GetStringAsync(decodedText);

                        XmlDocument doc = new();
                        doc.LoadXml(response);
                        string jsonText = JsonConvert.SerializeXmlNode(doc);
                        // Output or use the JObject as needed
                        jsonResponse = JsonConvert.DeserializeObject<ResValidateFakturPm>(JsonConvert.DeserializeObject<JObject>(jsonText)["resValidateFakturPm"].ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while converting XML to JSON.");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Failed to convert XML to JSON.");
                }

                return Ok(new
                {
                    Detected = true,
                    DecodedText = decodedText,
                    Points = points.Select(p => new { p.X, p.Y }).ToArray(),
                    XmlJsonResponse = jsonResponse // Deserialize JSON string to object for cleaner output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the PDF.");
                return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
            }
            finally
            {
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                // Clean up temporary files
                try
                {
                    if (System.IO.File.Exists(tempPdfFilePath))
                    {
                        System.IO.File.Delete(tempPdfFilePath);
                    }
                    if (System.IO.File.Exists(outputImageFilePath))
                    {
                        System.IO.File.Delete(outputImageFilePath);
                    }
                }
                catch (IOException ioEx)
                {
                    _logger.LogError(ioEx, "An error occurred while deleting temporary files.");
                }
            }

        }


        public class ResValidateFakturPm
        {
            public string? kdJenisTransaksi { get; set; }
            public string? fgPengganti { get; set; }
            public string? nomorFaktur { get; set; }
            public string? tanggalFaktur { get; set; }
            public string? npwpPenjual { get; set; }
            public string? namaPenjual { get; set; }
            public string? alamatPenjual { get; set; }
            public string? npwpLawanTransaksi { get; set; }
            public string? namaLawanTransaksi { get; set; }
            public string? alamatLawanTransaksi { get; set; }
            public string? jumlahDpp { get; set; }
            public string? jumlahPpn { get; set; }
            public string? jumlahPpnBm { get; set; }
            public string? statusApproval { get; set; }
            public string? statusFaktur { get; set; }
            public string? referensi { get; set; }
            public DetailTransaksi? detailTransaksi { get; set; }
        }

        public class DetailTransaksi
        {
            public string? nama { get; set; }
            public string? hargaSatuan { get; set; }
            public string? jumlahBarang { get; set; }
            public string? hargaTotal { get; set; }
            public string? diskon { get; set; }
            public string? dpp { get; set; }
            public string? ppn { get; set; }
            public string? tarifPpnbm { get; set; }
            public string? ppnbm { get; set; }
        }




    }
}

