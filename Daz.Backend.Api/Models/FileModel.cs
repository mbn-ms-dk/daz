using System;

namespace Daz.Backend.Api.Models;
    public class FileModel
    {
        public Guid FileId { get; set; } 
        public string FileName { get; set; } = string.Empty;
        public string FileCreatedBy { get; set; } = string.Empty;
        public DateTime FileCreatedDate { get; set; } = DateTime.Now;
    }

    public class AddFileModel
    {
        public string FileName { get; set; } = string.Empty;
        public string FileCreatedBy { get; set; } = string.Empty;
    }