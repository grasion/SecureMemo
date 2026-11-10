using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SecureMemo.Models;

namespace SecureMemo.Services
{
    public class ExportService
    {
        public void ExportToDocx(Memo memo, string outputPath) => ExportAllToDocx(new[] { memo }, outputPath);

        /// <summary>여러 메모를 하나의 Word 문서로 (메모마다 새 페이지)</summary>
        public void ExportAllToDocx(IEnumerable<Memo> memos, string outputPath)
        {
            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var first = true;
            foreach (var memo in memos)
            {
                if (!first)
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                first = false;

                // 제목
                body.AppendChild(TextParagraph(memo.DisplayTitle,
                    new RunProperties(new Bold(), new FontSize { Val = "32" })));

                // 날짜
                body.AppendChild(TextParagraph(
                    $"작성일: {memo.CreatedAt:yyyy-MM-dd HH:mm}   수정일: {memo.UpdatedAt:yyyy-MM-dd HH:mm}",
                    new RunProperties(new Color { Val = "808080" }, new FontSize { Val = "18" })));

                body.AppendChild(new Paragraph());

                // 내용 (줄마다 문단)
                foreach (var line in memo.Content.Replace("\r\n", "\n").Split('\n'))
                    body.AppendChild(TextParagraph(line));
            }

            mainPart.Document.Save();
        }

        private static Paragraph TextParagraph(string text, RunProperties? props = null)
        {
            var run = new Run();
            if (props != null) run.RunProperties = props;
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return new Paragraph(run);
        }

        public void ExportToText(Memo memo, string outputPath)
        {
            var content = $"{memo.Title}\n\n작성일: {memo.CreatedAt:yyyy-MM-dd HH:mm}\n\n{memo.Content}";
            File.WriteAllText(outputPath, content);
        }
    }
}
