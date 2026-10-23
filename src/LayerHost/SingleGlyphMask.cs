namespace LayerHost;

internal static class SingleGlyphMask
{
    internal static int[] Build(byte[] before,byte[] after,int width,int height)
    {
        if(width is <1 or >128||height is <3 or >32||before.Length!=width*height*4||after.Length!=before.Length)throw new ArgumentException("Invalid glyph calibration region");
        var changed=new bool[width*height];var columns=new int[width];
        for(int i=0;i<changed.Length;i++)if(Math.Abs(before[i*4]-after[i*4])+Math.Abs(before[i*4+1]-after[i*4+1])+Math.Abs(before[i*4+2]-after[i*4+2])>75){changed[i]=true;columns[i%width]++;}
        int[] mask=Enumerable.Range(0,changed.Length).Where(i=>changed[i]&&columns[i%width]<height-1).ToArray();
        if(mask.Length<10)throw new InvalidOperationException("Insufficient calibrated glyph pixels after excluding caret columns");return mask;
    }
}
