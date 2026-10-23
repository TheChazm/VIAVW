using System.Drawing.Imaging;

namespace LayerHost;

internal static class PreviewDamage
{
    // Null requests a complete repaint. Empty means the opaque frames are identical.
    // Compare every source byte, including padding/unused alpha: extra damage is
    // harmless, while sampling/hashing could miss a real visual change.
    internal static unsafe Rectangle[]? Compare(Bitmap? before,Bitmap after)
    {
        if(ReferenceEquals(before,after))return [];
        if(before==null||before.Size!=after.Size||before.PixelFormat!=PixelFormat.Format32bppRgb||after.PixelFormat!=PixelFormat.Format32bppRgb)return null;
        var bounds=new Rectangle(Point.Empty,after.Size);
        var a=before.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
        try
        {
            var b=after.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            try
            {
                if(a.Stride<=0||b.Stride<=0)return null;
                const int tileWidth=64,tileHeight=32;
                int columns=(after.Width+tileWidth-1)/tileWidth,rows=(after.Height+tileHeight-1)/tileHeight;
                var changed=new bool[columns*rows];
                for(int y=0;y<after.Height;y++)
                {
                    var left=new ReadOnlySpan<byte>((void*)(a.Scan0+y*a.Stride),after.Width*4);
                    var right=new ReadOnlySpan<byte>((void*)(b.Scan0+y*b.Stride),after.Width*4);
                    if(left.SequenceEqual(right))continue;
                    int row=y/tileHeight*columns;
                    for(int x=0;x<columns;x++)
                    {
                        if(changed[row+x])continue;
                        int offset=x*tileWidth*4,length=Math.Min(tileWidth,after.Width-x*tileWidth)*4;
                        if(!left.Slice(offset,length).SequenceEqual(right.Slice(offset,length)))changed[row+x]=true;
                    }
                }
                var damage=new List<Rectangle>();
                for(int row=0;row<rows;row++)for(int column=0;column<columns;column++)
                {
                    if(!changed[row*columns+column])continue;
                    int first=column;while(column+1<columns&&changed[row*columns+column+1])column++;
                    damage.Add(Rectangle.FromLTRB(first*tileWidth,row*tileHeight,Math.Min(after.Width,(column+1)*tileWidth),Math.Min(after.Height,(row+1)*tileHeight)));
                }
                return damage.ToArray();
            }
            finally{after.UnlockBits(b);}
        }
        finally{before.UnlockBits(a);}
    }
    internal static Rectangle Scale(Rectangle source,Size sourceSize,Size drawnSize)
    {
        // Cover interpolation neighbours and fractional destination boundaries.
        return Rectangle.Intersect(new Rectangle(Point.Empty,drawnSize),Rectangle.FromLTRB(
            (int)Math.Floor((source.Left-2d)*drawnSize.Width/sourceSize.Width)-2,
            (int)Math.Floor((source.Top-2d)*drawnSize.Height/sourceSize.Height)-2,
            (int)Math.Ceiling((source.Right+2d)*drawnSize.Width/sourceSize.Width)+2,
            (int)Math.Ceiling((source.Bottom+2d)*drawnSize.Height/sourceSize.Height)+2));
    }
}
