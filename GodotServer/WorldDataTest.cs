using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using SphServer.Helpers;
using File = System.IO.File;

namespace SphServer
{

    public static class WorldDataTest
    {
        public static async Task SendWorldDataGirasAsync(NetworkStream ns)
        {
            var watcher = new FileSystemWatcher("C:\\source\\packets\\");
            watcher.Filter = "world_load_data.txt";
            watcher.EnableRaisingEvents = true;
            watcher.Changed += (_, _) =>
            {
                foreach (Process process in Process.GetProcessesByName("sphereclient_4"))
                {
                    process.Kill();
                }
            };
            // south gate incubator 32892
            // east incubator 62589, further incubator 56363 5103
            // northeast road incubator 56363, further incubator 18935, a bit north incubator 20291
            // west incubator 34376, 20556, 58817
            // southwest incubator 5081, 52678
            var somedata_1 =
                // incubator 11637 11644 20216
                "c3002c0100ac04d30b10808f835f5d0b46e0db28a42a56cd581dbe337d220640649101e0c7d78211f8541ca78af516508763f7c1880110596400f8ddd560043ed1c8a3e27471d261faf4416200441619007ef74918814fdd8daa781e6975988ebd871800914506801fbe1346e08bcba72aeece5c1dee92c2230640649101e037f48411f848a7b38a87d35987c33279880110596400f831416104be2e6aaee224f5d5610aa91b6200441619007e7c8018810fd3a0aa381d8d75f8fce290180091450600" +
                // incubator 43727 62927
                "bd002c0100ac0448861881af07e6ab3872d47678cf3d8a1800914506809fbc2446e05382612a1e6d671deeb040240640649101e0f7ac8a11f8e69aa88a17d2568753a4eb880110596400f819376304be7364ade27db1d561ccd5436200441619007e2bdc18812ff31ba9b855e176581481891800914506805f703946e02bb2f72a1eab821d76df5d230640649101e0d7478f11f8eee09b8a55a858870341d7880110596400f83dd76304be0339aae24abfd5e1ce4d3d62004416190000" +
                // npc 5503 5504
                "c8002c0100ac04d9131881cf13c6abd877fc75f855da8e180091450680dffb0446e05b44392a9ecc961df6ef55220640649101e0e73f8111f836a5b88a09815a874da611890110596400f8fd55500d3ef7aea8e2b5ddd56155382e624750140014433f000000853f70831b93c91103680440b8c18d7dd1c98591957dd1c985d9b101340408b48601007e801554834f2333aa38757775b8b6a48b7818140500c59010000040e10fdce0c66472c4001a01106e70635f74726164655f747261766c000d0102ad610000" +
                // npc 5509 5510 mob 27744 23255 incubator 62927
                "21002c0100ac04851554830fa2c6aa38d4767518bfad8df811140500c5500e0000" +
                // "40e10fdce0c66268c4001a01106e70635f74726164655f61726d6f72000d0182ab6100
                // 809f6105d5e09b929a2a26f24d1dd67bb7224e0445014031a4030000
                // 50780337b8b1181a804600841bdcd8179d5c1859d9575bd859da18404380206b1800e007c68634f818f5a78a3b914b87610dc6887b232e415c829284a3fb0500bf6b2da4c1e73f3f559c585c3a5c2b4146fc1c7109e21294241cdd2f0000" +
                // mob 21759 25945 & mapbook
                "c9002c0100ac04ff5448834f0a7faa7808b874f837888c983ae212c425284938ba5f00f0cb2a431a7cf8d753c5dca0a5c3a0c265c4e91197202e4149c2d1fd0280dfdb3d9de10320a14b02000000000000003850649101a00003d091008509002808028012228002000040411002286500140000000a822090650ba000000050108481ae220005000080822010487f03280000001404a180081f40010000a0200806cc47000a000000054138f0890050000000280802c29b368002000040411012be5e001400000000" +
                // smth else prob & recipebook
                "c3002c0100ac046ff77446411014eeaf01140000000a82b0d0ee0ca000000050100406953a0005000080822034b4d001280000001404c121550e40010000a0900500ffffffff3fa33834c307404297040000000000000070a0c82203400106a023010a1300501004802b478016000080822004480c01b4000000140441e0ed1fa0050000a02008038243002d0000000541201038046801000028084241533b400b00004041100c447200020000000a8270f07d031000000050100484ba7c8000000000" +
                // smth else & mantrabook
                "c6002c0100ac04467168464110128cd701020000000a82a0a0c80e1000000050108485f975800000008082203060b203040000001404a142941da0050000e027bc0e67f80048e89200000000000000009c1559640028c0007424406102000a820070bf0e8004000050108400fb75002d000080822008e4af03a8000000140461807f1d60000000a0200804f8eb002a00000005412860490748000000280882c1ff3a000300004041100e04d8011e0000000a828010ca0e300000005010040500760006000000" +
                // smth else & large bag        
                "c3002c0100ac04c2eb70464110160ad8012a0000000a82c010c00ec00000005010840602768043000080822038f0ae033c0000001404e1c10916200a0000e037c50e64f80048e89200000000000000009c1559640028c00074244061";
            var somedata_2 =
                "02000a8200b0c00e40fcffff5f108400fd75002f00008082200844b0030400000014046140821d600f0000a0200804d7a400a20000000541384080072006000028080282053f402000004041101414d8017c0100000a82b0b09e0f0006000050c80280ffffff7f" +
                // smth else
                "c8002c0100ac040bec54860f80842e0900000000000000004591450680020c4cb103142600a02008000cec001f000000054108b060079000000028088280013b000b000040411006e8f2012c0000000aaf3046f7351736f6354703f0d35fa7327c002474490000000000000000c68a2c32001460608a1da030010005410008460458000000280842c0dc3b40040000404110044ae0011a0000000a823040080fb001000050788531baafb9b0b1af3918809f043b95e10320a14b0200000000000000405164910100" +
                // smth else
                "bf002c0100ac0412ec54460106a6d8010a130050100400d771000500008082200414c00324000000140441c0001ee0050000a02008035cec0007000000855718a3fb9a0b1bfb9aab01f85d9352193e0012ba24000000000000000014451619000a3030c50e5098008082200018250370010000140421e0d21bc0000000a0200802f6fd00070000000541186076073800000028bcc218ddd75cd8d8d7dc0cc00f019ecaf00190d0250100000000000000a028b2c800508081297680c2040000" +
                // smth else
                "c0002c0100ac0408f054464110006ca601260000000a8210d0c00ec0050000501004010a53801000008082200c90bd0378000000145e618cee6b2e6cec6b2e06e067c14f65f80048e8920000000000000000501459640028c0c0143b406102000a820030c00e20010000501084804d73800400008082200804d803c4000000140461c0931b60000000a0f00a63745f7361635f733400bff57c2ac307404297040000000000000080a2c82203400106a6d8010a130050100400bb790005000000" +
                // smth else
                "c7002c0100ac04ebf9544641100494bb01240000000a823000b4042001000050788531baafb9b0b1af3919801f7a1f1ee00320a14b02000000000000000050649101a00003d09100070000000000000000000010b5e766330d02287cc1fec040d5c02f811d0ff00190d02501000000000000000028b2c800508001e84880030000000000000000000088daf3339b068115bee07e20b216e087578d07f80048e8920000000000000000001459640028c0007424c0010000000000000000000044edf91c4e83e001" +
                // door 2291 & smth else
                "c3002c0100ac0478d57840e10b0808faaa007ee67a948c0f80842e0900000000000000004091450680020c404702142600a0900500ffffffff3f1944c4c107404297040000000000000000a0c82203400106a023010a1300501004801f44000f00008082200434dd01e8030000140441e0030f20130500a0200803530b00540b0000054120402107a0a5000028084241153900a901004041100cbc5f01640000000a8270e0d203d0070000f09b47a0077c575151c5b845acc3034768c43f882c320000" +
                // door 2293 2294 2295 2296 banker 4213
                "c9002c0100ac04f308f440e11918ec12896110138938b7e78a0b4f00000000f0ab4780077c881c51c5358fadc39a2667c440882c3200149e40750200e0678f000ff89a37a28a6b1e5b877516cd888110596400283cc1ea0400c0ef1e011ef0fd6e4415d73cb60e8bd497110321b2c800507802d60900801f3e023de0cbdca12a8e90441d96397823e647649101a0f00cb70c54c5e991aec314eb71c48527f086a400fed541100e3eca7aa8e21ddfd561141e31e22150140014431501000085470070831b93c9210300" +
                // npc06 5184 telep 5233 & amulet shield
                "ca002c0100ac04751084432360c10d6eec4b2ccc6dad4c0ea0214000350c00f003a2a0197cf73456c5f5a3a7c3915e63c4bea028002886420300000a4fb002b20252f80310109015101090c22b7800f0000000f0000414340220dce0c6bee2eacae6e8bee8d2e8d8ca001a020454c30000bf380a7ec0a74e38551c026a3aec2326464c81c82203c08f1c8d77f1015eb725014cc0240320f11c0328a8c86c82a428c000742440210b00feffffff152e05003fa0";
            var somedata_3 =
                "75e8c5475d78614c331796cc981581bcaf882213861106" +
                // armor belt gloves left_bracelet
                "b7002c0100ac0440ebd04b0106a023010a5900f0ffffffaf702100f845cc932f3e20d0b824c01d456400428063004516998358548001e84880421600fcffffffffce1590171f109a5c12507442320036a93180028accd1082dc000742440210b00feffffff152e0500bfee7ae6c5072cd996043449920c4073730ca0c8223332420b30001d0950c80280ffffffff1fa83ff7e203085a4b029489490600d03b065064913987450518808e04286401c0ffffff7fc1030200" +
                // right_bracelet left_top_ring left_bottom_ring right_top_ring
                "bb002c0100ac04f1f3dc8b0f78382d09386e2319402af318409145e61c161560003a12a0900500ffffffffbfaa7af0c507244f9704b8e8890c00165df4af9822b30d8b0a30001d0950c80280ffffffffc23ba8dd7d91a585b5c9a5b99dcdcc00fc3ee9c1171f00095d12000000000000000080228bcc1e4889020c40470214b200e0ffffffbff00e6a775f6469616d72696e673039003f8c7ef0c507d89b9604708a920c008c5a04a09822b30d8b0a30001d0950c80280ffffff7f" +
                // pants right_bottom_ring boots spec 
                "c4002c0100ac0418fde04be11dd4eebec8d2c2dae4d2dcce6666007e35f0e08b0fd89f2e09c0e0f818004cbd18409145660fa4440106a023010a5900f0ffffff5f7807b5bb2fb2b4b036b934b733981c809f1539f6e203e4784b02383f4a06c0903c6e503c91c917a10518808e04286401c0ffffffff6ff11d78f1016da8250125b42503e0fd1bb72b8ac8d463d0020c40470214b200e0ffffff5fe14200f02bee04fa7c002474490000000000000000008a2c3279b0a80003d09100852c00f8ffffff07" +
                // smth else
                "c2002c0100ac044a88fc8a0f80842e09000000000000000040914566f4111560e0ed1ea0900500ffffffff3f4a197ec507404297040000000000000000a0c82273fb880a30f0760f50c80280ffffffff5f962dbfe20320a14b020000000000000000506491d97d44051878bb07286401c0ffffff7fe10d0002380134020a3f2063035378838080538082805f5711bfe20320a14b020000000000000000506491c97d44051878bb07286401c0ffffff7fe10d0002fe00a4000a7f20a30e0f2e5f0e0f" +
                // smth else
                "c6002c0100ac045d45fc4ae10d0202fe00aa000a6f202060033008507883810112807680c21b10108801bc0314dea0a0e024600fa0f006060620012601853738382004c80528bc01028232002f40e10d1212b801ec010a6fa0901009000df093fee6577c002474490000000000000000008a2c32a58fa800036ff700852c00f8ffffffff11e1f32b3e0012ba24000000000000000000451699d347548081b77b80421600fcffffffff988ff8151f00095d12000000000000000080228b4cea232ac0c0db3d00" +
                // smth else
                "c4002c0100ac04cc47fc4a210b00feffffff7f3e11fc8a0f80842e09000000000000000040914566f5111560e0ed1ea0900500ffffffffbf376d7ec507404297040000000000000000a0c822d3fa880a30f0760f50c80280ffffffffdfd70bbfe20320a14b020000000000000000506491097d44051878bb07286401c0ffffffffeffe9a5ff10190d02501000000000000000028b2c8d43ea2020cbcdd0314b200e0ffffffffd7eeccaff80048e892000000000000000000145964661f510106deee0100" +
                // smth else
                "c4002c0100ac04edcefc4a210b00feffffff7f2a75fc8a0f80842e090000000000000000409145e6f5111560e0ed1ea0900500ffffffffbf163a7ec507404297040000000000000000a0c82213fb880a30f0760f50c80280ffffffff5faa1cbfe20320a14b020000000000000000506491097d44051878bb07286401c0ffffff7fe10d000248014a010a6f1010a008e00850780381004d805c80c21b0c041803fc0114de8020e01ac013a0f0060501ed00940085373008c007380228bcc141c045804000" +
                // smth else    
                "bc002c0100ac04a972fc4ae10d1002ce0190020a6f9010600a2011507803850033808a80c21b2c04a4020c0514de8021a00d002ca0f0060d0999005300bf2b4748c607404297040000000000000000a0c822034001068c";
            var somedata_4 =
                "e2000a2c100050f88680b40c008082b8080000800180c233440f0000b40e0000400f0000147e804281e2273104c9f80048e8920000000000000000001459640028c080511c40810502000adf409096010050f0ffffff0f200050f80400b7080000bb080000" +
                // smth else
                "bd002c0100ac041243904ce119a20700005a070000000000000a3f40a100f07bfb87647c002474490000000000000000008a2c32001460c0280ea0c0020100856f2038cb000018f8ffffff071000287c0200f8020040fa020040e1199e0700002c070000000000000a3f40a100f0131c82647c002474490000000000000000008a2c32001460c0280ea0c0020100856f2040cb000020f8ffffff071000287c02401b0300801b030040e1199407000042070000000000000a3f40410100" +
                // smth else
                "be002c0100ac040287908c0f80842e0900000000000000004091450680020c18c50114582000a0f00d016919000005701100000003008567881e0000681d0000801e000028fc008502c5afa91d92f10190d02501000000000000000028b2c800508001a33880020b040014be21202d0300a0c0300200006000a0f00cd1030000af030000cd030000851fa028c8f889e440323e0012ba24000000000000000000451619000a306014075060810080c237082c6700000cfcffffff03080000" +
                // smth else
                "bd002c0100ac042239904ce1130282180000881800000acfd03c0000603900000000000050f801050a80dff70d24e30320a14b02000000000000000050649101a0000346710005160800287c43005a060000811b030000800040e1199407000042070000000000000a3f404101f0abcb87647c002474490000000000000000008a2c32001460c0280ea0c0020100856f2030cb00001000000000001000287c42405304004053040040e1192c07000094070000000000000a3fe0e10100" +
                // smth else
                "c5002c0100ac04c6eb908c0f80842e0900000000000000004091450680020c18c50114582000a0f00d02cc19000004ffffffff000200854f00e06a0000306b0000283c83e50000c0f200000000000040e1071e32007e8aec908c0f80842e0900000000000000004091450680020c18c50114582000a0f00d04681900000400000000000200854f084889000048890000283c83e5000080f200000000000040e1073c3c007ef3eb908c0f80842e0900000000000000004091450680020c18c5011458200000" +
                // smth else
                "c7002c0100ac04f3eb904ce11b049033000000feffffff0104000a9f1020bc0000b0bc0000507806e6010080c201000000000080c20f140400fc30d921191f00095d12000000000000000080228b0c000518308a0328b0400040e11b02d23200000ae82200000006000acf103d0000d03a0000003d000050f8010a058a9f283b24e30320a14b02000000000000000050649101a0000346710005160800287cc34041060040c1ffffff3f800040e11300e01a0000f01a00000acf60390000003d00000000000000" +
                // smth else
                "c1002c0100ac04a2ec904ce1071428007ef7eba08f0f80842e0900000000000000a05c9145e64612156040781da0c00218003ffb75d0c707405b97040047690c008054e4a1c8227324890a3020bc0e5060010f805ffe3ae8e303e8ba4b020033380600d02b2e576491a992440518105e0728b08003c08f7f1df4f101bed9250180fa1e030026165d28b2c8f44aa2020c08af0314582000e087bf0efaf80085019200c63d920140fa909b155964162451010684d7010a2ce000a0d022d017085000" +
                // smth else
                "b7002c0100ac042ce9a08f0f20c62e0900e2dd180050b4685f9145e65512156040781da0c0020300bfff75d0c707b04097040092610c006049d4acc8227320890a3020bc0e50600102809f003be8e30320a14b0200000000000000405164911995440518105e0728b04001c02f941df4f10190d0250100000000000000942bb2c8e448a2020c08af0314582000e007c00efaf80048e8920000000000000000ca155964d22451010684d7010a2c4000a0d022a8b38d4f00" +
                // ink thing & else
                "cd002c0100ac0405eca08f4ffeffcde89a61ee";
            var somedata_5 =
                "9833eba508409145e64112156040781da0c0020700bf0076d0c707404297040000000000000050aec822f326890a3020bc0e50600102801f013be8e30352a14b0200c92c06003a2b2a5764919994440518105e0728b0400bc08f771df4f18184442643a7cf28c3dc1528952bb2c8244aa2020c08af031458a00040a14560df2da0f07382057d7c40c67349004003c500002f44448a2c32f991a80003c2eb000516d800f821b0032c3e0012ba2400000000000000805a41910100" +
                // smth else
                "c6002c0100ac0408ec004b0106a023010a5900f0ffffffff636007817d002474490000000000000000b58a2c3200146060811da0900500ffffffff0516f800283c01e10100c0cf829de2f0011dad2501ea442303d0271def2bb2c8b416a2020c2cb00314b200e0ffffffbfc00212003f037610d807404297040000000000000030afc8220340010616d8010a5900f0ffffff5f60011680c213d81d0000fce8f241601f00095d120000000000000080ab228b0c000518586007286401c0ffffff7f81052c0000" +
                // smth else
                "c4002c0100ac0474f92070e109fa0e00007ec18820b00f80842e0900000000000000205891450680020ce8af0314b200e0ffffffbfc0020b00852740380000f8cdbd83c03e0012ba2400000000000000805c451619000a30a0bf0e50c80280ffffffff020b4400149e60e10000e057020f02fb0048e8920000000000000000681459640028c080fe3a40210b00feffffff0b2cd000507802850300801f213c08ec0320a14b02000000000000000051649101a00003faeb00852c00f8ffffff2fb0c00600" +
                // smth else
                "c5002c0100ac0484f02070e109120e00007e11ec2c8b0f80842e09000000000000000040914506a2020c4cb10314b200e0ffffffbff0040000feff857f3023fb9aa37b1b5b334b9b43030000f8b98e83c03e0012ba24000000000000000000451619000a3020c10e50c80280ffffffff020b2800149e40f00000e057000f02fb0048e8920000000000000000001559640028c080043b40210b00feffffff0b2c9000507882c20300809f013c08ec0320a14b0200000000000000e050649101a0000312ec00" +
                // smth else
                "c4002c0100ac0406f02070210b00feffffff0b2cf002507882c30300801f173b08ec0320a14b02000000000000001853649101a0000312ec00852c00f8ffffff2fb0c00140e109060f00007e46c920b00f80842e0900000000000000a04391450680020c5c930214b200e0ffffffbfc0025c008527e83a0000f85d7a83c03e0012ba24000000000000008009451619000a30704d0a50c80280ffffffff020b1800149e60ee0000e067df0f02fb0048e8920000000000000000821559640028c0c0352900" +
                // smth else
                "c4002c0100ac04f6fd2070210b00feffffff0b2c7000507882ba0300801fb33b08ec0320a14b0200000000000000d850649101a00003d7a400852c00f8ffffff2fb0c00140e109ee0e00007e36d320b00f80842e0900000000000000004091450680020c20c00314b200e0ffffffbfc00213008527e03b0000f835b083c03e0012ba24000000000000000058451619000a3080000f50c80280ffffffff020b7001149e00ef0000e047610a02fb0048e8920000000000000000dc1459640028c000023c00" +
                // smth else
                "c4002c0100ac0414a62070210b00feffffff0b2c1002507882890300801fd93b08ec0320a14b02000000000000003055649101a0000308f000852c00f8ffffff2fb0800740e109240e00007e03ec20b00fdefba1a8a8706368f38ea2d85e91450680020c58f00314b200e0ffffffbfc00212008527103a0000f86d9a83c03e0012ba24000000000000000014451619000a3060c10f50c80280ffffffff020b2400149e20f00000e017600f02fb0048e8920000000000000000b01459640028c080053f00" +
                // smth else
                "c7002c0100ac0401f62070210b00feffffff0b2c1003507882840300809f2737e8e30320a14b020000000000000000506491c992440518b0e00728b04000c04f811d2cf10190d02501000000000000000028b2c8441ca2020c4cb10314b200";
            var somedata_6 =
                "e0ffffffbfc00201003fbb79aec307404297040000000000000000a0c822d353880a30b09e0f50c80280ffffffff020b2800fc94bbc1171f00095d12000000000000000080228bcc00488f020cace70314b200e0ffffffbff00e6a775f6469616d72696e67313500" +
                // key key 1st_slot (mission maybe too?)
                "c3002c0100ac04404be08b0f80842e0900000000000000004091452600a4470106d6f3010a5900f0ffffff5f7807b5bb2fb2b4b036b934b7b3981a801fa73fbde20320a14b02000000000000006054649101a00003d09100852c00f8ffffff2f7cc2e50400b51d800005c04fb29d5ef10190d02501000000000000005829b2c800508001e84880421600fcffffff173e017302a0da0e608002e067f04e71f854f5d58d3b82ae91fd24ae8e8d155964f661510106a023010a5900f0ffffff5f6081dc24" +
                // slot 2-6
                //"c9002c0100ac0407dbd0870f08a42c09e08d0e190078ae88489145665d831560003a12a0900500ffffffffbf29388ac3074c409704a8388b0c001c54ecadc8223345880a30001d0950c80280ffffffff020b9001143c20e0b7f64971f80048e8920000000000000000e6155964ba08510106a023010a5900f0ffffff5f60010d80820704fc10be390e1f90675a12a01d3832006fcc3180228b4c19212ac000742440210b00feffffff0b2cf03e50f080809fbe23c5e10320a14b020000000000000058516491592244" +
                Convert.ToHexString(BitHelper.BinaryStringToByteArray(
                    (File.ReadAllText("C:\\source\\packets\\world_load_data.txt")).RemoveLineEndings())) +
                // slot 7-10
                "bd002c0100ac04fa8e14470106a023010a5900f0ffffff5f608152815fbd3bc5e10320a14b020000000000000028576491b927440518808e04286401c0ffffff7f8105b0047ed4c8c08b0f80842e090000000000000000409145261e031760003a12a0900500ffffffffbf38218ac307404297040000000000000000a0c8227346880a30001d0950c80280ffffffff020b6000fcaac2290e1f00095d1200000000000000c0bc228b4c0f212ac000742440210b00feffffff0b2c501100" +
                // spec abilities 1-3 speedhack_mantra
                "c9002c0100ac0405f34c9f0f80842e0900000000000000e0589145a611161560003a12a0900500ffffffff0a2d020075b08b1fc43cd1e70320a14b0200000000000000805164910985450518808e04506811c0d40a8ffc0ac0883e1f00095d120000000000000080a3228b4c2d2c2ac000742480428b20711dece277544bf4f90048e89200000000000000009a155964d26b510106a02301145a04f1e9c823bf1f44aec3074c1f9804f840a50cc0f588fcafc8223354880a3020830850c80280ffffffff020b780000" +
                // smth else
                "c3002c0100ac044d77dc8b0ff8286ef84da8f2d877c9c418408145261d161560400611a0900500ffffffff050f08f87de0411fbeebc2896329fcf663b4dcc562014516997f0d568001194480421600fcffffffffa616a0171f00095d12000000000000000080228bcc0a4699020cc8200214b200e0ffffffff87428e7df8eaed208a6237368646142a8a7f145864da645101066410010a5900f0ffffffffab22275f7c002474490000000000000000008a2c328b245d0a3020830850c80280ffffff7f" +
                // helmet island_token
                "c8002c0100ac04deafd88e0f80842e09000000000000000040914566b0021560400611a0900500ffffffff3f971ef0c507a0e7960470848b0c8015730ca09822730d8b0a3020830850c80280ffffffffc23ba8dd7d91a585b5c9a5b99dcdcc00fc48df40031f00000000000000000000000000228b0c000518808e04286401c0ffffff7fe10500127e3f82788f0f80842e0900000000000000004091452641101560003a12a0900500ffffffff050f08f8954ae31a3e0012ba240000000000000000004516190000" +
                // smth else
                "1b002c0100ac04a5d2b8460106a023010a131050c80280ffffff7f17002c0100ac04c6061d5fa12eab0c6910047ae3c1763a";
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_1));
            // Thread.Sleep(10);
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_2));
            // Thread.Sleep(10);
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_3));
            // Thread.Sleep(10);
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_4));
            // Thread.Sleep(10);
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_5));
            // Thread.Sleep(10);
            // await ns.WriteAsync(ConvertHelper.FromHexString(somedata_6));
        }

        public static async Task SendWorldDataKnelseAsync(NetworkStream ns)
        {
            var data_1 =
                "C8002c0100ac04210C10802F809FE02946E0335EA92956EC8923F2989E2B0640649101E057788A11F88AF5688A4BDAEB88A0CCE68A0110596400F811A062043E9BB1A0626EEF3AA2ECDFB96200441619007E46DD18814F6040A57850F98EC84671AE1800914506809F533746E013DEBF290EB0C32332EC9D2B0640649101E027FF8D11F80CB8798AFF6DF288784EE88A0110596400F8E97F63043EC3D19BE27F9B3C225E1ABA6200441619007ECF1454838F19B6A698EE3A91A81073AEB810140500C5D032000000" +
                "BD002c0100ac04CF145443E10FDCE0C66472C6001A01106E70635F74617665726E6B65657072000D0182AA6100805FD02B90E10320A14B0200000000000000D056649101A0000321B300850900280802007E2840EBFFFF7F411002846101F20000000A8220F0E309900E0000501084017450804A000080822010147902480200001404C1E01816800E0000A0200807DFB00012000000054140609C056000000028088242E32C80020000404110161C6701180000000A5900F0FFFFFF0F" +
                "C8002c0100ac04F8A154860F80842E0900000000000000E05E91450680020C04BD02142600A02008008DAC0018000000054108507A05C0000000280882400C2CC010000040411006706101440000000AAF3046F7351736F6352703F01386A5327C002474490000000000000000C68A2C3200146020E815A03001000541002086052001000028084240B8248003000040411004C42501180000000A8230402E099000000050788531BAAFB9B0B1AFB91980DF8F2795E10320A14B0200000000000000305664910100" +
                "BF002c0100ac043F9E54460106825E010A130050100400204F80220000808220048C6F024001000014044140DA15C0000000A020080349AF001C000000855718A3FB9A0B1BFB9A8B01F8A18352193E00A8BA24005046630000FD6263451619000A3010F40A50980080822000B0830274000000140421203F14A0030000A020080246AF0014000000054118407A05A800000028BCC218DDD75CD8D8D7DC0CC0AFC893CAF0013CC5250120BF1C030084191B2BB2C800508081A05780C2040000" +
                "BD002c0100ac04459E5446411000EE4301360000000A8210C00B0B10010000501004015F58800900008082200C14C30284000000145E618CEE6B2E6CEC6BAE06E0770C4B65F800A6E99200E0A58E01C0258D8D1559640028C040D02B406102000A8200900C0B3001000050108400C559800A0000808220082CCE0234000000140461801B16A0010000A0F00A63745F7361635F7331003FCD594AC607404297040000000000000000A0C822034001064266010A130050C80280FFFFFF7F" +
                "CA002c0100ac049BB388830F80842E0900000000000000004091450680020C84CC02142600A02008009CB30096000000054108E89C055000000028088280E72C80E80500404110063E6701640000000A8240103A0B406A000050108402D15900AF0000808220188CCE02901A00001404E1807416000F0000E0B73B000FF8B4166B8A55771289EAA9E78A8110596400283CC1E50400C08F77001EF071CFD41477EB2412E557CF150321B2C800507802CC0900809FF2003CE093C0AD29EE9E4D24B2AC9F2B064064910100" +
                "C9002c0100ac04CA03F040E111FEFFFFFF2D0A62800BCF50FDEA504422CC31C4247F51FC7B7605FA7C002474490000000000000000008A2C3205B2A8000321B300852C00F8FFFFFFFF35B282C03EA021BA240050C66200C0766221451619000A30801F0A50C80280FFFFFFFF020B6000149E40F00000E0A7F40A02FB0048E89200000000000000006E1559640028C0007E2840210B00FEFFFFFF0B2C8001507802BD0300805F0C2C08EC0320A14B0200000000000000F853649101A00003F8A100852C00F8FFFFFF07" +
                "C9002c0100ac0431B02070810586000A4FA0780000F0C38505817D002474490000000000000000568A2C32001460003F14A0900500FFFFFFFF05161001283C01E00100C08F181604F60190D0250100000000000000E42AB2";
            var data_2 =
                "C800508001615880421600FCFFFFFF17588004A0F00408070000BF704910D8074042970400000000000000F0ACC822034001068461010A5900F0FFFFFF5F60010780C213281C0000FCC42541601F00095D1200000000000000408F228B0C000518108605286401C0FFFFFF7F8105180000" +
                "C6002c0100ac04E2922070E109160E00007EE49220B00F80842E0900000000000000C05991450680020C08C30214B200E0FFFFFFBFC0020900852748380000F8017982C03E0012BA24000000000000000000451619000A30F0E30950C80280FFFFFFFF020B1401149EE0ED0000E037BE0902FB80D8EB9200683C8E01A09F8C2D1559640028C0C08F2740210B00FEFFFFFF0B2C0005507802B80300809FB42BC5E10320A14B0200000000000000D0576491492D440518F8F104286401C0FFFFFF7F81050C0000" +
                "BB002c0100ac0449AF1C870F80842E0900000000000000004091452685101560E0C713A0900500FFFFFFFF0516E000F8B18382C03E00F9B9240050066300C0B6E25C451619000A30800E0A50C80280FFFFFFFF020B7400149E80EF0000E0971F0A02FB00E4E7920040198C0100DB8A871559640028C0003A2840210B00FEFFFFFF0B2CD0015078029D0300809FD12B08EC03A4A44B0280973606006C2FBE55649101A00003E8A000852C00F8FFFFFF2FB0000540E109EE0E000000" +
                "C0002c0100ac0448AF20B00FF0B42E0900F9E51800F4CA584491450680020CA0830214B200E0FFFFFFBFC00215008527C03B0000F8DD8782C03EE0E2B82400E4576300E80E636C451619000A3050E40950C80280FFFFFFFF020B6C00149E40EB0000E0C70B0B02FB808BE39200905F8D01A03B8CEB1459640028C040912740210B00FEFFFFFF0B2C1001507802A50300809F2F2C08EC0320A14B02000000000000000050649101A00003459E00852C00F8FFFFFF2FB0C00440E109FA0E000000" +
                "C0002c0100ac04C5B020B00FC8782E090094B11800B09D785F91450680020C14790214B200E0FFFFFFBFC00221008527B03B0000F825C382C03E4069B9240050866300A0336372451619000A30700C0B50C80280FFFFFFFF020B4C00149E80F00000E0A7380B02FB00A5E5920040198E0180CE8CF71459640028C0C0312C40210B00FEFFFFFF0B2C5001507882C1030080DFE22C08EC03769B4B02C0B0370680C9320650649101A00003C7B000852C00F8FFFFFF2FB0400340E109120F000000" +
                "BE002c0100ac04DCB020B00FD86D2E0900C3DE180026CB184091450680020C1CC30214B200E0FFFFFFBFC0020D008527F83B0000F87DC382C03E60B7B924000C7B6300982C6357451619000A3010F40A50C80280FFFFFFFF020B4800149EE0F10000E0C7380BBEF80048E892000000000000000000145964923F62146020E815A0900500FFFFFFFF857750BBFB224B0B6B934B733B8B9101F835CE822F3E205FB6240071FF63006C6B6300451699D88F200518087A05286401C0FFFFFF3F" +
                "C8002c0100ac048DB3E04BE11DD4EEBEC8D2C2DAE4D2DCCE626C007E8EB3E08B0F48BD2D094012F71800C7D518409145E6F8A3460106825E010A5900F0FFFFFF5F7807B5BB2FB2B4B036B934B7B3981980DFE32CBDE20320A14B02000000000000000050649101A0000321B300852C00F8FFFFFF2F7C82E204806B1B400005C04F72965EF10190D02501000000000000000028B2C800508081905980421600FCFFFFFF173E617102E0B50D208002E057390B7DF80048E8920000000000000000001459642A345200" +
                "C6002c0100ac0495B3D04701064266010A5900F0FFFFFFFFB39C25FA7C002474490000000000000000008A2C3219B2A8000321B300BFCB59A2CF07404297040000000000000070A1C822D3228B0A3010320BA0D022A042ADCBF861CE127D3E0012BA24000000000000000000451699F45A5480819059801FE72C5AE20320A14B020000000000000000506491093D440518D89C05286401C0FFFFFF7F810502007E9DB3E08B0FC8972D0940DCFF1800DBDA1840914566F62345010636";
            var data_3 =
                "67010A5900F0FFFFFF0F" +
                "C8002c0100ac049DB3E04BE11DD4EEBEC8D2C2DAE4D2DCCE6260007E9EB3D0870F80842E0900000000000000404B9145E63D131560607316A0900500FFFFFFFFBFCF5974C707404297040000000000000000A0C822135F010A30B0390B50C80280FFFFFFFF5FE82CF9E20320A14B0200000000000000005064910920E9528081CD5980421600FCFFFFFFFF4467B1171F10D85A124070113200FDC53180228B4C1E062EC0C0E62C40210B00FEFFFFFF7FA3B3E48B0F80842E090000000000000000409145E6982405" +
                "B4002c0100ac04A3B3E44B01063667010A5900F0FFFFFFFF239DE55D7C002474490000000000000000008A2C325B9FA800039BB300852C00F8FFFFFFFF99CE82063E00000000000000000000000000441619000A3010320B50C80280FFFFFFFFC20B0008FC4E67F11E1F00095D12000000000000000080228BCC80202AC040C82C40210B00FEFFFFFF0B1E10F04B9DC5357C002474490000000000000000008A2C32001460206416A0300101852C00F8FFFFFF07";

            // await ns.WriteAsync(ConvertHelper.FromHexString(data_1));
            // Thread.Sleep(10);
            //
            // await ns.WriteAsync(ConvertHelper.FromHexString(data_2));
            // Thread.Sleep(10);
            //
            // await ns.WriteAsync(ConvertHelper.FromHexString(data_3));
            // Thread.Sleep(10);

        }
    }
}