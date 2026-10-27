using System.Buffers.Binary;
using Pharaoh.Engine.Buildings;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;
using Pharaoh.Engine.Walkers;
using Pharaoh.Desktop.Legacy.Managers;
using Pharaoh.Desktop.Legacy.UI;
using Pharaoh.Assets.Text;

namespace Pharaoh.Desktop.Legacy;

internal sealed partial class PharaohWindow
{
    private int? mapInformationTile;
    private int mapInformationBuilding;
    private int mapInformationClickY;
    private int mapInformationCategory;
    private int mapInformationHover=-1;
    private int mapInformationPage;
    private int mapInformationWalker;
    private bool mapInformationOrders;
    private bool mapInformationHelp;
    private MessageEntry? mapInformationHelpMessage;
    private int mapInformationHelpScroll;
    private readonly List<Walker> mapInformationWalkers=[];
    private readonly Dictionary<int,Bitmap> mapInformationImages=[];

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    private bool OpenMapInformation(UiPoint point)
    {
        if(activeMissionSession is not {} session || !LayoutManager.CityMapBounds(presentationLogicalWidth,presentationLogicalHeight,compactCitySidebar).Contains(point.X,point.Y) || !cameraManager.TryHitTest(point.X,point.Y,out int tile))return false;
        mapInformationTile=tile;mapInformationClickY=point.Y;
        LegacyResourceAvailability.Refresh(session);
        mapInformationBuilding=LegacyMapInformation.BuildingAt(session.LogicalChunks,tile);
        timeManager.SetInspectionPause(true);inputManager.Clear();
        mapInformationOrders=false;mapInformationHelp=false;mapInformationPage=0;mapInformationWalker=0;mapInformationHover=-1;
        uint terrain=BinaryPrimitives.ReadUInt32LittleEndian(session.LogicalChunks["terrain_grid"].Span[(tile*4)..]);
        if((terrain&0x1000)!=0)mapInformationBuilding=0;
        bool floodGrass=(session.LogicalChunks["bitfields_grid"].Span[tile]&0x80)!=0;
        bool rubble=session.LogicalChunks.TryGetValue("rubble_building_type_grid",out var rubbleGrid)&&rubbleGrid.Span[tile]!=0&&mapInformationBuilding==0;
        mapInformationCategory=LegacyMapInformation.TerrainCategory(terrain,floodGrass,rubble);
        mapInformationWalkers.Clear();
        if(citySimulation is not null)
            foreach(int delta in LegacyMapInformation.WalkerNeighborOffsets)
                foreach(Walker walker in citySimulation.Walkers.Active.Where(w=>w.Tile==tile+delta && !w.IsHidden && (int)w.Type is not (0 or 6 or 36 or 37 or 59 or 60 or 61 or 62 or 63 or 65 or 71 or 72 or 74)))
                    if(mapInformationWalkers.Count<7)mapInformationWalkers.Add(walker);
        var records=session.LogicalChunks["walkers"].Span;
        for(int id=1;id<records.Length/388&&mapInformationWalkers.Count<7;id++)
        {
            var record=records.Slice(id*388,388);
            if(record[14]!=1||record[10] is 0 or 6 or 36 or 37 or 59 or 60 or 61 or 62 or 63 or 65 or 71 or 72 or 74||InfoShort(record,56)==4||mapInformationWalkers.Any(w=>w.Id==id)||citySimulation?.Walkers.Active.Any(w=>w.Id==id)==true)continue;
            int walkerTile=BinaryPrimitives.ReadInt32LittleEndian(record[32..]);
            if(Array.Exists(LegacyMapInformation.WalkerNeighborOffsets,offset=>walkerTile==tile+offset))mapInformationWalkers.Add(LegacyWalkerInformation.ReadInspectionRecord(id,record));
        }
        CloseCityMenu();openBuildMenuCommand=null;cityHoverTile=null;
        Invalidate();return true;
    }

    [OriginalAddress(0x004F96E0,"FUN_004f96e0")]
    private void CloseMapInformation()
    {
        if(mapInformationHelpMessage is not null){CloseHelp();return;}
        if(mapInformationOrders){mapInformationOrders=false;mapInformationHover=-1;Invalidate();return;}
        mapInformationTile=null;mapInformationWalkers.Clear();mapInformationHover=-1;timeManager.SetInspectionPause(false);Invalidate();
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    private ReadOnlySpan<byte> MapInformationBuildingRecord()=>activeMissionSession!.LogicalChunks["buildings"].Span.Slice(mapInformationBuilding*264,264);

    [OriginalAddress(0x004F8960,"FUN_004f8960")]
    private Rectangle MapInformationBounds()
    {
        int rows=22;
        if(mapInformationBuilding>0)
        {
            int type=InfoShort(MapInformationBuildingRecord(),16);
            var definition=LegacyBuildingInfoDefinitions.Get(type);
            rows=type is 71 or 191?18:definition.Rows>0?definition.Rows:type is >=140 and <=149?16:22;
        }
        else if(mapInformationCategory is 7)rows=14;
        else if(mapInformationCategory is 8 or 9 or 12 or 23 or 24)rows=16;
        var b=LegacyMapInformation.Bounds(presentationLogicalWidth,presentationLogicalHeight,rows,mapInformationClickY,compactCitySidebar);
        if(mapInformationOrders)
        {
            int count=Math.Min(MapInformationResources().Length,MapInformationMaximumRows());
            return new(b.X,32,464,((count*4+2)/3+7)*16);
        }
        return new(b.X,b.Y,b.Width,b.Height);
    }

    [OriginalAddress(0x00502780,"FUN_00502780")]
    private int MapInformationMaximumRows()=>presentationLogicalHeight switch{600=>15,768=>21,_=>10};

    [OriginalAddress(0x005596D0,"FUN_005596d0")]
    private int[] MapInformationResources()
    {
        int type=InfoShort(MapInformationBuildingRecord(),16);
        return type is 71 or 191?LegacyFoodAvailability.Read(activeMissionSession!.LogicalChunks):
            type is 70 or 190?LegacyResourceAvailability.Read(activeMissionSession!.LogicalChunks).Where(r=>Array.IndexOf(LegacyDistributionRules.BazaarResources,r)>=0).ToArray():
            LegacyResourceAvailability.Read(activeMissionSession!.LogicalChunks);
    }

    [OriginalAddress(0x004F96E0,"FUN_004f96e0")]
    private int MapInformationHit(UiPoint point)
    {
        if(mapInformationHelpMessage is not null)return HelpHit(point);
        Rectangle b=MapInformationBounds();int x=point.X-b.X,y=point.Y-b.Y;
        if(new Rectangle(b.Width-40,b.Height-40,27,27).Contains(x,y))return 0;
        if(new Rectangle(14,b.Height-40,27,27).Contains(x,y))return 1;
        if(mapInformationHelp)return -1;
        if(mapInformationOrders)
        {
            int type=InfoShort(MapInformationBuildingRecord(),16);
            int count=Math.Min(MapInformationResources().Length,MapInformationMaximumRows());
            int acceptTop=b.Height-40+(type is 72 or 193?8:0);
            if(type is not (70 or 190) && new Rectangle(80,acceptTop,304,20).Contains(x,y))return 3;
            for(int row=0;row<count;row++)
            {
                int top=46+row*20;
                if(new Rectangle(type is 70 or 190?180:250,top,type is 70 or 190?230:165,20).Contains(x,y))return 100+row;
                if(type is 70 or 190)continue;
                if(new Rectangle(200,top+2,17,17).Contains(x,y))return 200+row;
                if(new Rectangle(217,top+2,17,17).Contains(x,y))return 300+row;
            }
            if(new Rectangle(414,74-b.Y,24,24).Contains(x,y))return 4;
            if(new Rectangle(414,b.Height-78,24,24).Contains(x,y))return 5;
        }
        else
        {
            if(mapInformationBuilding>0 && InfoShort(MapInformationBuildingRecord(),16) is 70 or 71 or 72 or 115 or 190 or 191 or 193 && new Rectangle(40,b.Height-40,27,27).Contains(x,y))return 6;
            if(mapInformationBuilding>0 && InfoShort(MapInformationBuildingRecord(),16) is 70 or 71 or 72 or 190 or 191 or 193 && new Rectangle(100,b.Height-34,272,20).Contains(x,y))return 2;
            if((mapInformationBuilding==0 || InfoShort(MapInformationBuildingRecord(),28)==0 && InfoShort(MapInformationBuildingRecord(),16) is >=10 and <=29) && y>=45 && y<97 && x>=25 && (x-25)%60<52 && (x-25)/60<mapInformationWalkers.Count)return 400+(x-25)/60;
        }
        return -1;
    }

    [OriginalAddress(0x004F96E0,"FUN_004f96e0")]
    [OriginalAddress(0x00406300,"FUN_00406300")]
    [OriginalAddress(0x00406460,"FUN_00406460")]
    private void ActivateMapInformation(MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Right){CloseMapInformation();return;}
        if(e.Button!=MouseButtons.Left||!TryToGame(e.Location,out UiPoint point))return;
        int action=MapInformationHit(point);
        if(mapInformationHelpMessage is not null){ActivateHelp(action);return;}
        if(action==0)CloseMapInformation();
        else if(action==1)
        {
            int type=mapInformationBuilding>0?InfoShort(MapInformationBuildingRecord(),16):0;
            int help=type is >=10 and <=29?56:type>0?LegacyBuildingInfoDefinitions.Get(type).HelpId:mapInformationWalkers.Count>0?42:mapInformationCategory switch{6=>57,7=>59,9 or 23 or 24=>85,11=>58,_=>10};
            OpenHelp(help>0?help:10);
        }
        else if(action==2){mapInformationOrders=true;mapInformationPage=0;mapInformationHover=-1;Invalidate();}
        else if(action is 4 or 5){int maximum=Math.Max(0,MapInformationResources().Length-MapInformationMaximumRows());mapInformationPage=Math.Clamp(mapInformationPage+(action==4?-1:1),0,maximum);Invalidate();}
        else if(action==6)ActivateCityMenuItem(3,InfoShort(MapInformationBuildingRecord(),16) is 72 or 193?4:5);
        else if(action==3)
        {
            foreach(int resource in MapInformationResources())LegacyMapInformation.Refuse(activeMissionSession!,mapInformationBuilding,resource);
            Invalidate();
        }
        else if(action>=100 && action<400)
        {
            int row=action%100;int[] resources=MapInformationResources();
            if(mapInformationPage+row>=resources.Length)return;
            int resource=resources[mapInformationPage+row];
            if(action<200)LegacyMapInformation.CycleOrder(activeMissionSession!,mapInformationBuilding,resource);
            else LegacyMapInformation.ChangeLimit(activeMissionSession!,mapInformationBuilding,resource,action<300?-800:800);
            Invalidate();
        }
        else if(action>=400){mapInformationWalker=action-400;Invalidate();}
    }

    [OriginalAddress(0x004F8E60,"FUN_004f8e60")]
    private void DrawMapInformation(Graphics g)
    {
        if(mapInformationHelpMessage is not null){DrawHelp(g);return;}
        if(mapInformationTile is null || activeMissionSession is null)return;
        Rectangle b=MapInformationBounds();uiSkin.DrawOuterPanel(g,b.X,b.Y,b.Width/16,b.Height/16);
        int type=mapInformationBuilding>0?InfoShort(MapInformationBuildingRecord(),16):0;
        var d=LegacyBuildingInfoDefinitions.Get(type);
        int group=type switch{70 or 190=>97,71 or 191=>98,72 or 193=>99,_=>d.TextGroup};
        string title=type is >=10 and <=29?(InfoShort(MapInformationBuildingRecord(),28)>0?InfoText(29,type-10):InfoText(128,0)):
            type>0?InfoText(group,d.TitleItem):InfoText(MapTerrainTextGroup(),MapTerrainTextGroup()==70?mapInformationCategory+10:0);
        if(mapInformationOrders)title=InfoText(group,type is 71 or 191?6:type is 70 or 190?7:3);
        headingFont.DrawCenteredAt(g,title,b.X+b.Width/2,b.Y+10);
        if(mapInformationHelp)menuFont.DrawWrapped(g,InfoText(group,1),new(b.X+32,b.Y+56,b.Width-64,b.Height-112));
        else if(mapInformationOrders)DrawMapStorageOrders(g,b);
        else if(type is >=10 and <=29)DrawMapHouseInformation(g,b);
        else if(type is 70 or 71 or 72 or 190 or 191 or 193)DrawMapStorageInformation(g,b,type);
        else if(type>0)DrawMapWorkplaceInformation(g,b,d);
        else DrawMapTerrainInformation(g,b);
        {
            InfoImage(g,695+(mapInformationHover==1?1:0),b.X+14,b.Bottom-40);
            if(!mapInformationOrders&&type is 70 or 71 or 72 or 115 or 190 or 191 or 193)InfoImage(g,(type is 72 or 193?442:443)+(mapInformationHover==6?1:0),b.X+40,b.Bottom-40);
            InfoImage(g,699+(mapInformationHover==0?1:0),b.Right-40,b.Bottom-40);
        }
    }

    [OriginalAddress(0x0050E030,"FUN_0050e030")]
    [OriginalAddress(0x00504070,"FUN_00504070")]
    [OriginalAddress(0x004FF740,"FUN_004ff740")]
    private void DrawMapWorkplaceInformation(Graphics g,Rectangle b,LegacyBuildingInfoDefinition d)
    {
        var building=MapInformationBuildingRecord();int type=InfoShort(building,16),employees=InfoShort(building,64);
        int needed=buildingCosts.GetModel(campaignDifficulty,type)?.Employees??0;
        int percentage=needed>0?employees*100/needed:100;
        int group=d.TextGroup,top=d.StaffingTop;
        bool road=LegacyMapInformation.HasRoad(activeMissionSession!.LogicalChunks,mapInformationBuilding);
        if(type==115)
        {
            InfoResource(g,8,b.X+32,b.Y+56);
            menuFont.DrawAt(g,$"{InfoText(154,13)} {Math.Max(0,(int)InfoShort(building,60))} {InfoText(8,11)}",b.X+60,b.Y+60);
            int status=employees<1?5:InfoShort(building,60)<1?11:percentage>=100?6:percentage>=75?7:percentage>=50?8:percentage>=25?9:10;
            if(LegacyMapInformation.IsProductionStopped(activeMissionSession!,building[67]))status=4;
            InfoParagraph(g,road?154:69,road?status:25,b,32,86,400,50);
        }
        else if(type is 81 or 167 or 55)
        {
            if(!road)InfoParagraph(g,69,25,b,32,56,400,70);
            else if(employees==0)InfoParagraph(g,group,9,b,32,56,400,70);
            else
            {
                InfoParagraph(g,group,InfoShort(building,42)>0?2:3,b,32,56,400,16);
                InfoParagraph(g,group,percentage>=100?4:percentage>=75?5:percentage>=50?6:percentage>=25?7:8,b,32,72,400,64);
            }
            if(type==167)InfoParagraph(g,group,1,b,32,b.Height-113,400,72);
        }
        else if(LegacyBuildingInfoProduction.Get(type) is {Maximum:>0} production)
        {
            int progress=Math.Clamp(InfoShort(building,84)*100/production.Maximum,0,100);
            menuFont.DrawAt(g,$"{InfoText(group,2)} {progress}% {InfoText(group,3)}",b.X+32,b.Y+(production.Input>0?40:44));
            if(production.Input>0)
            {
                InfoResource(g,production.Input,b.X+32,b.Y+56);
                menuFont.DrawAt(g,$"{InfoText(group,12)} {Math.Max(0,(int)InfoShort(building,60))} {InfoText(8,InfoShort(building,60)==1?10:11)}",b.X+60,b.Y+60);
            }
            int status=LegacyMapInformation.IsProductionStopped(activeMissionSession!,building[67])?4:employees<1?5:production.Input>0&&InfoShort(building,60)<1?11:percentage>=100?6:percentage>=75?7:percentage>=50?8:percentage>=25?9:10;
            InfoParagraph(g,road?group:69,road?status:25,b,32,production.Input>0?86:70,400,60);
            if(production.Input==0)InfoParagraph(g,group,1,b,32,b.Height-113,400,64);
        }
        else if(type is 47 or 51)
        {
            int resource=type==47?17:23;
            InfoResource(g,resource,b.X+20,b.Y+30);menuFont.DrawAt(g,$"{InfoShort(building,60)} {InfoText(23,resource)}",b.X+40,b.Y+35);
            InfoParagraph(g,road?group:69,road?(employees<1?2:InfoShort(building,60)<1?4:3):25,b,32,56,400,70);
        }
        else if(LegacyBuildingInfoProduction.UsesServiceStaffState(type))
        {
            if(type==53){InfoResource(g,23,b.X+20,b.Y+30);menuFont.DrawAt(g,$"{InfoShort(building,60)} {InfoText(23,23)}",b.X+40,b.Y+35);}
            InfoParagraph(g,road?group:69,road?(employees>0?3:2):25,b,32,56,400,70);
        }
        else if(type is >=60 and <=69)
            InfoParagraph(g,group,1,b,32,40,400,48);
        else if(type is >=140 and <=149)
            InfoParagraph(g,161,d.TitleItem+1,b,32,b.Height-126,400,80);
        else if(type is 77 or 78 or 79)
        {
            InfoResource(g,21,b.X+16,b.Y+36);menuFont.DrawAt(g,$"{InfoText(106,2)} {InfoShort(building,184)} {InfoText(8,0)}",b.X+48,b.Y+47);
            InfoParagraph(g,103,1,b,32,b.Height-143,400,80);
        }
        else
        {
            if(needed>0&&!road)InfoParagraph(g,69,25,b,32,56,400,64);
            else if(d.Address!=0)InfoParagraph(g,group,1,b,32,56,400,Math.Max(48,(top>0?top: b.Height-56)-64));
        }
        if(top>0)DrawMapStaffing(g,b,top,d.StaffingRows,employees,needed);
    }

    [OriginalAddress(0x004FC5C0,"FUN_004fc5c0")]
    private void DrawMapStaffing(Graphics g,Rectangle b,int top,int rows,int employees,int needed,int callOffset=6)
    {
        uiSkin.DrawInnerPanel(g,b.X+16,b.Y+top,27,rows);
        var building=MapInformationBuildingRecord();int coverage=InfoShort(building,24);
        int status=employees<needed?(cityHudState?.Population<1?16:coverage<1?17:coverage>39?18:20):coverage>39?0:20;
        int baseline=top+callOffset+(status==0?16:10);
        InfoImage(g,709,b.X+40,b.Y+top+callOffset+6);
        normalFont.DrawAt(g,$"{employees} {InfoText(8,employees==1?12:13)} ({needed} {InfoText(69,0)}",b.X+60,b.Y+baseline);
        if(status!=0)normalFont.DrawWrapped(g,InfoText(69,status),new(b.X+70,b.Y+top+callOffset+26,360,Math.Max(16,rows*16-callOffset-26)));
    }

    [OriginalAddress(0x005014A0,"FUN_005014a0")]
    [OriginalAddress(0x00501FD0,"FUN_00501fd0")]
    [OriginalAddress(0x00500A40,"FUN_00500a40")]
    private void DrawMapStorageInformation(Graphics g,Rectangle b,int type)
    {
        var building=MapInformationBuildingRecord();int[] resources=MapInformationResources();
        int employees=InfoShort(building,64),needed=buildingCosts.GetModel(campaignDifficulty,type)?.Employees??(type is 71 or 191?12:6);
        bool road=LegacyMapInformation.HasRoad(activeMissionSession!.LogicalChunks,mapInformationBuilding);
        if(type is 71 or 191)
        {
            int stored=Enumerable.Range(1,8).Sum(r=>LegacyMapInformation.Stock(activeMissionSession!.LogicalChunks,mapInformationBuilding,r));
            if(road)
            {
            menuFont.DrawAt(g,$"{InfoText(98,2)} {stored} {InfoText(8,stored==1?16:17)}",b.X+34,b.Y+40);
            menuFont.DrawAt(g,$"{InfoText(98,3)} {InfoShort(building,86)} {InfoText(8,InfoShort(building,86)==1?16:17)}",b.X+220,b.Y+40);
            for(int i=0;i<resources.Length;i++)
            {
                int x=i<2?34:240,y=i%2==0?68:92;
                InfoResource(g,resources[i],b.X+x,b.Y+y);
                menuFont.DrawAt(g,$"{LegacyMapInformation.Stock(activeMissionSession!.LogicalChunks,mapInformationBuilding,resources[i])} {InfoText(23,resources[i])}",b.X+x+34,b.Y+y+7);
            }
            }
            else InfoParagraph(g,69,25,b,32,40,400,90);
            DrawMapStaffing(g,b,163,5,employees,needed,0);
            int cart=InfoShort(building,42);Walker? walker=citySimulation?.Walkers.Active.FirstOrDefault(w=>w.Id==cart);
            int status=walker is null?15:walker.ActionState is 15 or 16 or 18 or 19 or 20 or 21?33:16;
            if(employees>0)normalFont.DrawWrapped(g,InfoText(99,status),new(b.X+32,b.Y+200,400,40));
        }
        else if(type is 70 or 190)
        {
            if(!road)InfoParagraph(g,69,25,b,32,56,400,64);
            else if(employees<1)InfoParagraph(g,97,2,b,32,56,400,64);
            else
            {
                int[] foods=LegacyFoodAvailability.Read(activeMissionSession!.LogicalChunks);
                if(foods.Sum(r=>LegacyMapInformation.Stock(activeMissionSession.LogicalChunks,mapInformationBuilding,r))==0)InfoParagraph(g,97,4,b,32,45,400,40);
                for(int index=0;index<foods.Length;index++)
                {
                    InfoResource(g,foods[index],b.X+32+index*110,b.Y+84);
                    menuFont.DrawAt(g,LegacyMapInformation.Stock(activeMissionSession.LogicalChunks,mapInformationBuilding,foods[index]).ToString(),b.X+64+index*110,b.Y+90);
                }
                int[] goods=[13,19,17,15];
                for(int index=0;index<goods.Length;index++)
                {
                    InfoResource(g,goods[index],b.X+32+index*110,b.Y+104);
                    menuFont.DrawAt(g,LegacyMapInformation.Stock(activeMissionSession.LogicalChunks,mapInformationBuilding,goods[index]).ToString(),b.X+64+index*110,b.Y+110);
                }
            }
            DrawMapStaffing(g,b,128,5,employees,needed,14);
            if(employees>0)normalFont.DrawWrapped(g,InfoText(97,InfoShort(building,42)>0?11:10),new(b.X+32,b.Y+184,400,30));
        }
        else
        {
            int i=0;
            foreach(int resource in road?Enumerable.Range(1,35):Enumerable.Empty<int>())
            {
                int stock=LegacyMapInformation.Stock(activeMissionSession!.LogicalChunks,mapInformationBuilding,resource);if(stock==0)continue;
                int x=i<4?25:240,y=45+(i%4)*30;
                InfoResource(g,resource,b.X+x,b.Y+y);menuFont.DrawAt(g,$"{stock} {InfoText(23,resource)}",b.X+x+30,b.Y+y+7);i++;
            }
            if(!road)InfoParagraph(g,69,25,b,32,56,400,64);
            else if(i==0)InfoParagraph(g,type is 70 or 190?97:99,type is 70 or 190?4:22,b,32,56,400,64);
            DrawMapStaffing(g,b,183,5,employees,needed,5);
            if(employees>0)normalFont.DrawWrapped(g,InfoText(99,InfoShort(building,42)>0?16:15),new(b.X+32,b.Y+231,400,30));
        }
        Rectangle orders=new(b.X+100,b.Bottom-34,272,20);
        uiSkin.DrawButton(g,orders,mapInformationHover==2);
        menuFont.DrawCenteredAt(g,InfoText(type is 71 or 191?98:type is 70 or 190?97:99,type is 71 or 191?5:type is 70 or 190?6:2),b.X+232,b.Bottom-30);
    }

    [OriginalAddress(0x00501B60,"FUN_00501b60")]
    [OriginalAddress(0x00502990,"FUN_00502990")]
    private void DrawMapStorageOrders(Graphics g,Rectangle b)
    {
        int[] resources=MapInformationResources();int count=Math.Min(resources.Length,MapInformationMaximumRows());
        uiSkin.DrawInnerPanel(g,b.X+16,74,27,(count*4+2)/3);
        var building=MapInformationBuildingRecord();var storage=activeMissionSession!.LogicalChunks["building_storages"].Span;
        int storageOffset=building[193]*196;
        for(int row=0;row<count&&mapInformationPage+row<resources.Length;row++)
        {
            int resource=resources[mapInformationPage+row],y=83+row*20;
            InfoResource(g,resource,b.X+(InfoShort(building,16) is 72 or 193?25:32),y-5);
            selectedFont.DrawAt(g,InfoText(23,resource),b.X+(InfoShort(building,16) is 72 or 193?60:72),y-1);
            int mode=storageOffset+10+resource<storage.Length?storage[storageOffset+10+resource]:1;
            if(InfoShort(building,16) is 70 or 190)
            {
                InfoResource(g,resource,b.X+408,y-5);
                if(mapInformationHover==100+row)uiSkin.DrawButton(g,new(b.X+180,y-5,210,20),true);
                (mode==1?objectiveFont:selectedFont).DrawAt(g,InfoText(97,mode==1?9:8),b.X+(mode==1?240:260),y);
                continue;
            }
            int limit=storageOffset+((mode==2?124:52)+resource*2);
            int maximum=limit+2<=storage.Length?InfoShort(storage,limit):3200;
            bool yard=InfoShort(building,16) is 72 or 193;
            string order=mode switch{1=>InfoText(99,8),3=>InfoText(99,yard?21:20),_=>InfoText(99,mode==2?19:maximum>2400?18:32)+" "+InfoText(99,maximum<=800?25:maximum<=1600?26:maximum<=2400?27:mode==2?31:28)+(maximum<=2400?InfoText(99,yard?29:30):string.Empty)};
            if(mapInformationHover==100+row)uiSkin.DrawButton(g,new(b.X+180,y-5,250,20),true);
            (mode is 1 or 3?objectiveFont:selectedFont).DrawAt(g,order,b.X+250,y);
            if(mode is 0 or 2)
            {
                InfoImage(g,746+(mapInformationHover==200+row?1:0),b.X+200,y-3);
                InfoImage(g,743+(mapInformationHover==300+row?1:0),b.X+217,y-3);
            }
        }
        if(InfoShort(building,16) is 70 or 190)return;
        int acceptY=b.Bottom-40+(InfoShort(building,16) is 72 or 193?8:0);
        uiSkin.DrawButton(g,new(b.X+80,acceptY,304,20),mapInformationHover==3);
        menuFont.DrawCenteredAt(g,InfoText(99,7),b.X+232,acceptY+4);
        if(resources.Length>count)
        {
            g.DrawImageUnscaled(fileUpIcon,b.X+414,74);g.DrawImageUnscaled(fileDownIcon,b.X+414,b.Bottom-78);
        }
    }

    [OriginalAddress(0x0050E570,"FUN_0050e570")]
    [OriginalAddress(0x0050EFB0,"FUN_0050efb0")]
    [OriginalAddress(0x004FC150,"FUN_004fc150")]
    [OriginalAddress(0x004FC2D0,"FUN_004fc2d0")]
    [OriginalAddress(0x004FC3B0,"FUN_004fc3b0")]
    private void DrawMapHouseInformation(Graphics g,Rectangle b)
    {
        var building=MapInformationBuildingRecord();int population=InfoShort(building,28);
        if(population<1){DrawMapTerrainInformation(g,b);InfoParagraph(g,128,1,b,32,b.Height-113,400,64);return;}
        int reason=building[133];InfoParagraph(g,127,reason+40,b,32,36,400,66);
        int[] resources=LegacyFoodAvailability.Read(activeMissionSession!.LogicalChunks);
        for(int i=0;i<resources.Length;i++)
        {
            InfoResource(g,resources[i],b.X+32+i*50,b.Y+110);
            normalFont.DrawAt(g,InfoShort(building,86+resources[i]*2).ToString(),b.X+34+i*50,b.Y+132);
        }
        uiSkin.DrawInnerPanel(g,b.X+16,b.Y+148,27,10);
        InfoImage(g,708,b.X+34,b.Y+158);
        normalFont.DrawAt(g,$"{population} {InfoText(127,20)}",b.X+50,b.Y+168);
        normalFont.DrawAt(g,InfoText(127,building[78]==0?23:24),b.X+36,b.Y+194);
        int happiness=unchecked((sbyte)building[194]);
        normalFont.DrawAt(g,InfoText(127,happiness>49?26:happiness>39?27:happiness>29?28:happiness>19?29:happiness>9?30:happiness>0?31:32),b.X+36,b.Y+214);
    }

    [OriginalAddress(0x004FA530,"FUN_004fa530")]
    [OriginalAddress(0x004FA780,"FUN_004fa780")]
    [OriginalAddress(0x004FAB20,"FUN_004fab20")]
    private void DrawMapTerrainInformation(Graphics g,Rectangle b)
    {
        if(mapInformationBuilding==0 && mapInformationCategory is 7 or 8 or 9 or 12 or 23 or 24)
        {
            int group=MapTerrainTextGroup();
            int item=1;
            if(mapInformationCategory==7)
                item=activeMissionSession!.LogicalChunks["aqueduct_grid"].Span[mapInformationTile!.Value]==0?2:1;
            InfoParagraph(g,group,item,b,32,b.Height-(mapInformationCategory==7?128:mapInformationCategory==8?143:158),400,100);return;
        }
        uiSkin.DrawInnerPanel(g,b.X+16,b.Y+40,27,12);
        if(mapInformationWalkers.Count==0)
            normalFont.DrawCenteredAt(g,InfoText(70,0),b.X+232,b.Y+120);
        else
        {
            for(int i=0;i<mapInformationWalkers.Count;i++)
            {
                uiSkin.DrawButton(g,new(b.X+25+i*60,b.Y+45,52,52),i==mapInformationWalker);
                mapManager.DrawWalkerInspection(g,mapInformationWalkers[i],new(b.X+27+i*60,b.Y+47,48,48));
            }
            Walker walker=mapInformationWalkers[Math.Clamp(mapInformationWalker,0,mapInformationWalkers.Count-1)];
            var raw=activeMissionSession!.LogicalChunks["walkers"].Span.Slice(walker.Id*388,388);
            int wildlife=activeMissionSession.LogicalChunks["scenario_info"].Span[984];
            int deity=0;
            if(walker.HomeBuildingId>0)
            {
                int homeType=InfoShort(activeMissionSession.LogicalChunks["buildings"].Span,walker.HomeBuildingId*264+16);
                deity=homeType is >=60 and <=69?(homeType-60)%5:homeType is >=140 and <=149?(homeType-140)%5:0;
            }
            int nameGroup=BinaryPrimitives.ReadInt32LittleEndian(raw[152..]);
            if(nameGroup>0)campaignTitleFont.DrawAt(g,InfoText(nameGroup,InfoShort(raw,112)),b.X+90,b.Y+108);
            InfoGlobalImage(g,LegacyWalkerInformation.Portrait((int)walker.Type,raw[11],wildlife,deity),b.X+28,b.Y+112);
            string role=InfoText(64,(int)walker.Type);
            if(walker.Type is WalkerType.CartPusher or WalkerType.StorageTransporter or (WalkerType)38 && walker.HomeBuildingId>0)
                role=InfoText(41,InfoShort(activeMissionSession.LogicalChunks["buildings"].Span,walker.HomeBuildingId*264+16))+InfoText(64,4);
            normalFont.DrawWrapped(g,role,new(b.X+92,b.Y+139,340,20));
            if(walker.Resource>0)
            {
                InfoResource(g,walker.Resource,b.X+92,b.Y+150);
                normalFont.DrawAt(g,$"{walker.Cargo} {InfoText(23,walker.Resource)}",b.X+126,b.Y+154);
            }
            int speech=LegacyWalkerInformation.SpeechGroup((int)walker.Type,raw[11],wildlife,deity);
            int reason=BinaryPrimitives.ReadInt32LittleEndian(activeMissionSession.LogicalChunks["city_data"].Span[36632..]);
            int item=LegacyWalkerInformation.SpeechItem((int)walker.Type,walker.ActionState,unchecked((sbyte)raw[125]),raw[124],reason);
            if(speech>0&&item>=0)normalFont.DrawWrapped(g,InfoText(speech,item),new(b.X+90,b.Y+176,320,62));
        }
        if(mapInformationCategory is not (6 or 13))InfoParagraph(g,70,mapInformationCategory+36,b,36,b.Height-113,400,65);
    }

    [OriginalAddress(0x004FE300,"FUN_004fe300")]
    [OriginalAddress(0x004FE3C0,"FUN_004fe3c0")]
    [OriginalAddress(0x004FE490,"FUN_004fe490")]
    [OriginalAddress(0x004FE560,"FUN_004fe560")]
    [OriginalAddress(0x004FE630,"FUN_004fe630")]
    [OriginalAddress(0x005058F0,"FUN_005058f0")]
    private int MapTerrainTextGroup()=>mapInformationCategory switch{7=>141,8=>140,9=>139,12=>79,23=>165,24=>166,_=>70};

    [OriginalAddress(0x004D6160,"FUN_004d6160")]
    private void InfoParagraph(Graphics g,int group,int item,Rectangle b,int x,int y,int width,int height)=>menuFont.DrawWrapped(g,InfoText(group,item),new(b.X+x,b.Y+y,width,height));

    [OriginalAddress(0x004D62F0,"FUN_004d62f0")]
    private string InfoText(int group,int item)=>text.GetString(group,item)??string.Empty;

    [OriginalAddress(0x0053E590,"FUN_0053e590")]
    private void InfoImage(Graphics g,int image,int x,int y)=>uiSkin.DrawGeneralImage(g,image,x,y);

    [OriginalAddress(0x0053E590,"FUN_0053e590")]
    private void InfoResource(Graphics g,int resource,int x,int y)=>InfoGlobalImage(g,23239+resource,x,y);

    [OriginalAddress(0x0053E590,"FUN_0053e590")]
    private void InfoGlobalImage(Graphics g,int image,int x,int y)
    {
        if(!mapInformationImages.TryGetValue(image,out Bitmap? bitmap))
        {
            if(!graphicsAssets.GlobalImages.TryResolve(image,out _))return;
            bitmap=RgbaBitmap.Create(graphicsAssets.DecodeGlobal(image));mapInformationImages.Add(image,bitmap);
        }
        g.DrawImageUnscaled(bitmap,x,y);
    }

    [OriginalAddress(0x004F7E50,"FUN_004f7e50")]
    private static short InfoShort(ReadOnlySpan<byte> bytes,int offset)=>BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]);
}
