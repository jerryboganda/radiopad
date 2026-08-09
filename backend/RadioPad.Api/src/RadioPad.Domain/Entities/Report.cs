using System;
using System.Collections.Generic;

namespace RadioPad.Domain.Entities;

public partial class Report : Entity
{
    public string RadiologyId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public int PatientAge { get; set; }
    public string PatientGender { get; set; } = "";

    public ICollection<DictationAudio> Dictations { get; set; } = new List<DictationAudio>();
}
