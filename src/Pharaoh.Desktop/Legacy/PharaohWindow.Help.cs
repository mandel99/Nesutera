using Pharaoh.Assets.Text;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy;

internal sealed partial class PharaohWindow
{
    private readonly Stack<(MessageEntry Message, int Scroll)> helpHistory = new();
    private readonly List<(Rectangle Bounds, int Target)> helpLinks = [];
    private int helpMaximumScroll;
    private Rectangle helpUpBounds,helpDownBounds;
    private bool IsInformationVisible => mapInformationTile is not null || mapInformationHelpMessage is not null;

    [OriginalAddress(0x004C5BF0, "FUN_004c5bf0")]
    private void OpenHelp(int id, bool followingLink = false)
    {
        MessageEntry? next = messages.GetEntry(id);
        if (next is null || string.IsNullOrEmpty(next.Title)) return;
        if (followingLink && mapInformationHelpMessage is {} previous)
            helpHistory.Push((previous,mapInformationHelpScroll));
        else helpHistory.Clear();
        mapInformationHelpMessage=next;mapInformationHelp=true;mapInformationHelpScroll=0;mapInformationHover=-1;
        timeManager.SetInspectionPause(true);inputManager.Clear();CloseCityMenu();Invalidate();
    }

    [OriginalAddress(0x004C7340, "FUN_004c7340")]
    private void CloseHelp()
    {
        mapInformationHelpMessage=null;mapInformationHelp=false;mapInformationHelpScroll=0;
        helpHistory.Clear();helpLinks.Clear();mapInformationHover=-1;
        timeManager.SetInspectionPause(mapInformationTile is not null);Invalidate();
    }

    [OriginalAddress(0x004C5E60, "FUN_004c5e60")]
    private Rectangle HelpBounds()
    {
        var m=mapInformationHelpMessage!;
        return new((presentationLogicalWidth-640)/2+m.BoxX,(presentationLogicalHeight-480)/2+m.BoxY,m.BoxWidth*16,m.BoxHeight*16);
    }

    [OriginalAddress(0x004C72B0, "FUN_004c72b0")]
    private int HelpHit(UiPoint point)
    {
        Rectangle b=HelpBounds();
        if(new Rectangle(b.Right-38,b.Bottom-36,24,24).Contains(point.X,point.Y))return 0;
        if(helpHistory.Count>0&&new Rectangle(b.X+16,b.Bottom-36,90,24).Contains(point.X,point.Y))return 503;
        if(helpUpBounds.Contains(point.X,point.Y))return 501;
        if(helpDownBounds.Contains(point.X,point.Y))return 502;
        foreach(var link in helpLinks)if(link.Bounds.Contains(point.X,point.Y))return 1000+link.Target;
        return -1;
    }

    [OriginalAddress(0x004C72B0, "FUN_004c72b0")]
    private void ActivateHelp(int action)
    {
        if(action==0)CloseHelp();
        else if(action==503&&helpHistory.TryPop(out var previous))
        {mapInformationHelpMessage=previous.Message;mapInformationHelpScroll=previous.Scroll;Invalidate();}
        else if(action>=1000)OpenHelp(action-1000,true);
        else if(action is 501 or 502)ScrollHelp(action==501?-1:1);
    }

    [OriginalAddress(0x004C72B0, "FUN_004c72b0")]
    private void ScrollHelp(int delta)
    {mapInformationHelpScroll=Math.Clamp(mapInformationHelpScroll+delta,0,helpMaximumScroll);Invalidate();}

    [OriginalAddress(0x004C5E60, "Message pictures use bank296 or expansion bank23908")]
    [OriginalAddress(0x004C8070, "Inline message graphics use the same primary image bank")]
    private Bitmap? ResolveHelpGraphic(int graphic)
    {
        if(graphic<=0)return null;
        int global=graphic<1000?295+graphic:23908+graphic-1001;
        if(!graphicsAssets.GlobalImages.TryResolve(global,out _))return null;
        if(!mapInformationImages.TryGetValue(global,out var bitmap))
        {bitmap=RgbaBitmap.Create(graphicsAssets.DecodeGlobal(global));mapInformationImages.Add(global,bitmap);}
        return bitmap;
    }

    [OriginalAddress(0x004C5E60, "FUN_004c5e60")]
    [OriginalAddress(0x004C8070, "FUN_004c8070")]
    [OriginalAddress(0x004C8590, "FUN_004c8590")]
    private void DrawHelp(Graphics g)
    {
        var m=mapInformationHelpMessage!;Rectangle b=HelpBounds();
        uiSkin.DrawOuterPanel(g,b.X,b.Y,m.BoxWidth,m.BoxHeight);
        if(m.TitleX==0)headingFont.DrawCenteredAt(g,m.Title,b.X+b.Width/2,b.Y+(m.TitleY==0?14:m.TitleY));
        else headingFont.DrawAt(g,m.Title,b.X+m.TitleX,b.Y+m.TitleY);
        int contentTop=m.TitleX==0?48:32;
        void Picture(int image,int x,int y)
        {
            if(image<=0)return;
            Bitmap? bitmap=ResolveHelpGraphic(image);
            if(bitmap is null)return;
            g.DrawImageUnscaled(bitmap,b.X+x,b.Y+y);contentTop=Math.Max(contentTop,y+bitmap.Height+8);
        }
        Picture(m.Picture1,m.Picture1X,m.Picture1Y);Picture(m.Picture2,m.Picture2X,m.Picture2Y);
        if(m.CaptionX!=0&&!string.IsNullOrEmpty(m.Caption))
        {menuFont.DrawWrapped(g,m.Caption,new(b.X+m.CaptionX,b.Y+m.CaptionY,b.Width-m.CaptionX-16,80));contentTop=Math.Max(contentTop,m.CaptionY+32);}
        int visible=Math.Max(1,(b.Height-contentTop-48)/16-2);
        var tokens=LegacyHelpContent.Parse(m.Text);
        List<(LegacyHelpToken Token,int X,int Line,int Rows)> Layout(int width)
        {
            List<(LegacyHelpToken,int,int,int)> output=[];int x=0,line=0;
            foreach(var token in tokens)
            {
                if(token.Command=='P'){if(x>0)line++;x=50;continue;}
                if(token.Command=='L'){line++;x=0;continue;}
                if(token.Command=='G')
                {if(x>0)line++;int rows=(ResolveHelpGraphic(token.Graphic)?.Height??0)/16+2;output.Add((token,0,line,rows));line+=rows;x=0;continue;}
                int size=selectedFont.Measure(token.Text);
                if(x>0&&x+size>=width){line++;x=0;}
                output.Add((token,x,line,1));x+=size+6;
            }
            return output;
        }
        var items=Layout(b.Width-48);int total=items.Count==0?0:items.Max(i=>i.Line+i.Rows);
        bool scroll=total>visible;
        if(scroll){items=Layout(b.Width-80);total=items.Count==0?0:items.Max(i=>i.Line+i.Rows);}
        helpMaximumScroll=Math.Max(0,total-visible);mapInformationHelpScroll=Math.Min(mapInformationHelpScroll,helpMaximumScroll);
        int cols=m.BoxWidth-(scroll?4:2),panelRows=visible+1;
        uiSkin.DrawInnerPanel(g,b.X+16,b.Y+contentTop,cols,panelRows);
        helpLinks.Clear();var state=g.Save();g.SetClip(new Rectangle(b.X+19,b.Y+contentTop+3,cols*16-6,panelRows*16-6));
        foreach(var item in items)
        {
            if(item.Line+item.Rows<=mapInformationHelpScroll||item.Line>=mapInformationHelpScroll+visible)continue;
            int x=b.X+24+item.X,y=b.Y+contentTop+6+(item.Line-mapInformationHelpScroll)*16;
            if(item.Token.Command=='G')
            {var bitmap=ResolveHelpGraphic(item.Token.Graphic);if(bitmap is not null)g.DrawImageUnscaled(bitmap,b.X+16+(cols*16-bitmap.Width)/2,y+8);continue;}
            (item.Token.Link>=0?hoverFont:selectedFont).DrawHelpAt(g,item.Token.Text,x,y);
            if(item.Token.Link>=0)helpLinks.Add((new(x,y-3,selectedFont.Measure(item.Token.Text),16),item.Token.Link));
        }
        g.Restore(state);
        helpUpBounds=helpDownBounds=Rectangle.Empty;
        if(scroll)
        {
            int x=b.Right-46,top=b.Y+contentTop;
            helpUpBounds=new(x,top,26,26);helpDownBounds=new(x,top+(panelRows-2)*16-10,26,26);
            g.DrawImageUnscaled(mapInformationHover==501?fileUpHoverIcon:fileUpIcon,x,top);
            g.DrawImageUnscaled(mapInformationHover==502?fileDownHoverIcon:fileDownIcon,x,top+(panelRows-2)*16-10);
            int trackTop=top+26,trackHeight=(panelRows-2)*16-36;
            g.DrawImageUnscaled(scrollThumb,x+2,trackTop+(helpMaximumScroll>0?trackHeight*mapInformationHelpScroll/helpMaximumScroll:0));
        }
        if(helpHistory.Count>0)
        {
            InfoGlobalImage(g,12005+(mapInformationHover==503?1:0),b.X+16,b.Bottom-36);
            menuFont.DrawAt(g,InfoText(12,0),b.X+52,b.Bottom-31);
        }
        InfoImage(g,699+(mapInformationHover==0?1:0),b.Right-38,b.Bottom-36);
    }
}
