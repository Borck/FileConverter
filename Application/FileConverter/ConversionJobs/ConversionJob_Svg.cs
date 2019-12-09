using System;
using System.Drawing.Imaging;
using FileConverter.Annotations;
using FileConverter.Diagnostics;
using Svg;
using Svg.Transforms;

namespace FileConverter.ConversionJobs
{
  public class ConversionJob_Svg : ConversionJob
  {


    [UsedImplicitly]
    public ConversionJob_Svg()
    {
    }



    public ConversionJob_Svg(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
    {
    }



    //    protected override ConversionJob_Office.ApplicationName Application {
    //      get {
    //        return ConversionJob_Office.ApplicationName.PowerPoint;
    //      }
    //    }



    protected override bool IsCancelable()
    {
      return false;
    }



    protected override int GetOutputFilesCount()
    {
      return 1;
    }



    protected override void Initialize()
    {
      base.Initialize();

      if (this.State == ConversionState.Failed)
      {
        return;
      }

      this.ValidatePreset();
    }



    private void ValidatePreset()
    {
      if (this.ConversionPreset == null)
      {
        throw new InvalidOperationException("The conversion preset must be valid.");
      }
    }



    protected override void Convert()
    {
      this.ValidatePreset();

      var outFormat = this.GetOutputImageFormat();
      Debug.Log($"Convert SVG document to {outFormat}");


      this.UserState = Properties.Resources.ConversionStateReadDocument;
      var svg = SvgDocument.Open(this.InputFilePath);
      
      Debug.Log($"SVG meta (width:{svg.Width}, height:{svg.Height}, ppi:{svg.Ppi})");

      this.Transform(svg);

      this.UserState = Properties.Resources.ConversionStateConversion;
      var bitmap = svg.Draw();
      bitmap.Save(this.OutputFilePath, outFormat);
    }



    private ImageFormat GetOutputImageFormat()
    {
      if (this.ConversionPreset.OutputType == OutputType.Png)
      {
        return ImageFormat.Png;
      }
      throw new ArgumentException($"Output format {this.ConversionPreset.OutputType} is not supported by { this.GetType().Name}");
    }



    private void Transform(SvgDocument svg)
    {
      var expectedWidth = svg.Width;
      var expectedHeight = svg.Height;

      var transform = svg.Transforms ?? new SvgTransformCollection();
      this.Rotate(transform, ref expectedWidth, ref expectedHeight);
      this.Scale(transform, ref expectedWidth, ref expectedHeight);
      this.ClampImageMaximumSize(transform, ref expectedWidth, ref expectedHeight);
      this.ClampSizePowerOf2(transform, ref expectedWidth, ref expectedHeight);
      svg.Transforms = transform;
    }



    private void ClampSizePowerOf2(SvgTransformCollection transform, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      if (!this.TryGetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2, out bool clamp) ||
          !clamp)
      {
        return;
      }

      var lMax = Math.Max(expectedWidth.Value, expectedHeight.Value);
      if (lMax <= 0)
      {
        return;
      }

      var lClampPowerOf2 = 1 << ((int)Math.Log(lMax, 2) - 1);
      var scale = lClampPowerOf2 / lMax;
      ApplyScale(scale, transform, ref expectedWidth, ref expectedHeight);
      Debug.Log($"Clamp size to the nearest power of 2 size: {expectedWidth.Value}x{expectedHeight.Value} (by using scale factor: {scale})");
    }



    private void ClampImageMaximumSize(SvgTransformCollection transform, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      if (!this.TryGetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageMaximumSize, out int maxSize))
      {
        return;
      }

      float lMax = Math.Max(expectedWidth.Value, expectedHeight.Value);
      if (lMax > maxSize && maxSize > 0)
      {
        var scale = maxSize / lMax;
        ApplyScale(scale, transform, ref expectedWidth, ref expectedHeight);
        Debug.Log($"Clamp maximum size of {expectedWidth.Value}x{expectedHeight.Value} (by using scale factor: {scale})");
      }
    }



    private void Scale(SvgTransformCollection transform, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      if (this.TryGetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageScale, out float scale)
          && Math.Abs(scale - 1f) >= 0.05f)
      {
        ApplyScale(scale, transform, ref expectedWidth, ref expectedHeight);
        Debug.Log("Apply scale factor: {0}%.", scale * 100);
      }
    }



    private void Rotate(SvgTransformCollection transform, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      if (this.TryGetSettingsValue(ConversionPreset.ConversionSettingKeys.ImageRotation, out float phiDeg) &&
          Math.Abs(phiDeg - 0f) >= 0.05f)
      {
        float phiRad = (float)(phiDeg / 180 * Math.PI);

        Debug.Log($"Apply rotation: {phiDeg}°");
        transform.Add(new SvgTranslate(-expectedWidth.Value / 2, -expectedHeight.Value / 2));
        transform.Add(new SvgRotate(phiDeg));
        this.CalcSizeAfterRotation(phiRad, ref expectedWidth, ref expectedHeight);
        transform.Add(new SvgTranslate(expectedWidth.Value / 2, expectedHeight.Value / 2));
      }
    }



    private void CalcSizeAfterRotation(float phiRad, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      //https://iiif.io/api/annex/notes/rotation/

      var phiRadNorm = Mod(phiRad, Math.PI);
      double piHalf = Math.PI / 2;
      if (phiRad >= piHalf)
      {
        Swap(ref expectedWidth, ref expectedHeight);
        phiRadNorm -= piHalf;
      }
      expectedWidth = expectedWidth * (float)Math.Cos(phiRadNorm) + expectedHeight * (float)Math.Sin(phiRadNorm);
      expectedHeight = expectedWidth * (float)Math.Sin(phiRadNorm) + expectedHeight * (float)Math.Cos(phiRadNorm);
    }



    private static void ApplyScale(float scale, SvgTransformCollection transform, ref SvgUnit expectedWidth, ref SvgUnit expectedHeight)
    {
      transform.Add(new SvgScale(scale));
      expectedWidth *= scale;
      expectedHeight *= scale;
    }



    private static void Swap<T>(ref T lhs, ref T rhs)
    {
      var temp = lhs;
      lhs = rhs;
      rhs = temp;
    }



    /// <summary>
    /// Modulo to positive result
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    private static double Mod(double a, double b)
    {
      return (a % b + b) % b;
    }



    private bool TryGetSettingsValue<T>(string key, out T value)
    {
      var relevant = this.ConversionPreset.IsRelevantSetting(key);
      value = relevant
        ? this.ConversionPreset.GetSettingsValue<T>(key)
        : default(T);
      return relevant;
    }
  }
}
