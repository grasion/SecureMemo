using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SecureMemo.Models;

namespace SecureMemo.Services
{
    public class ExportService
    {
        public void ExportToDocx(Memo memo, string outputPath)
        {
            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // 제목
            var titlePara = body.AppendChild(new Paragraph());
            var titleRun = titlePara.AppendChild(new Run());
            titleRun.AppendChild(new Text(memo.Title));
            titleRun.RunProperties = new RunProperties(new Bold(), new FontSize { Val = "32" });

            // 날짜
            var datePara = body.AppendChild(new Paragraph());
            var dateRun = datePara.AppendChild(new Run());
            dateRun.AppendChild(new Text($"작성일: {memo.CreatedAt:yyyy-MM-dd HH:mm}"));

            // 빈 줄
            body.AppendChild(new Paragraph());

            // 내용
            var contentPara = body.AppendChild(new Paragraph());
            var contentRun = contentPara.AppendChild(new Run());
            contentRun.AppendChild(new Text(memo.Content));

            mainPart.Document.Save();
        }

        public void ExportToText(Memo memo, string outputPath)
        {
            var content = $"{memo.Title}\n\n작성일: {memo.CreatedAt:yyyy-MM-dd HH:mm}\n\n{memo.Content}";
            File.WriteAllText(outputPath, content);
        }
    }
}
