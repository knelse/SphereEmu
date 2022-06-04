USE [sph]
GO

/****** Object:  Table [dbo].[characters]    Script Date: 04/06/2022 23:55:52 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[characters](
	[max_hp] [int] NOT NULL,
	[max_mp] [int] NOT NULL,
	[strength] [int] NOT NULL,
	[agility] [int] NOT NULL,
	[accuracy] [int] NOT NULL,
	[endurance] [int] NOT NULL,
	[earth] [int] NOT NULL,
	[air] [int] NOT NULL,
	[water] [int] NOT NULL,
	[fire] [int] NOT NULL,
	[pdef] [int] NOT NULL,
	[mdef] [int] NOT NULL,
	[karma] [int] NOT NULL,
	[max_satiety] [int] NOT NULL,
	[title_level] [int] NOT NULL,
	[degree_level] [int] NOT NULL,
	[title_xp] [int] NOT NULL,
	[degree_xp] [int] NOT NULL,
	[current_satiety] [int] NOT NULL,
	[current_hp] [int] NOT NULL,
	[current_mp] [int] NOT NULL,
	[available_stats_title] [int] NOT NULL,
	[available_stats_degree] [int] NOT NULL,
	[gender_is_female] [bit] NOT NULL,
	[name] [nvarchar](max) NOT NULL,
	[face_type] [int] NOT NULL,
	[hair_style] [int] NOT NULL,
	[hair_color] [int] NOT NULL,
	[tattoo] [int] NOT NULL,
	[boots_model] [int] NOT NULL,
	[pants_model] [int] NOT NULL,
	[armor_model] [int] NOT NULL,
	[helmet_model] [int] NOT NULL,
	[gloves_model] [int] NOT NULL,
	[deletion_is_not_requested] [bit] NOT NULL,
	[x] [decimal](18, 0) NOT NULL,
	[y] [decimal](18, 0) NOT NULL,
	[z] [decimal](18, 0) NOT NULL,
	[turn] [decimal](18, 0) NOT NULL,
	[id] [int] IDENTITY(1,1) NOT NULL,
	[player_id] [int] NOT NULL,
	[index] [int] NOT NULL,
	[helmet_slot] [int] NOT NULL,
	[amulet_slot] [int] NOT NULL,
	[spec_slot] [int] NOT NULL,
	[armor_slot] [int] NOT NULL,
	[shield_slot] [int] NOT NULL,
	[belt_slot] [int] NOT NULL,
	[gloves_slot] [int] NOT NULL,
	[left_bracelet_slot] [int] NOT NULL,
	[right_bracelet_slot] [int] NOT NULL,
	[pants_slot] [int] NOT NULL,
	[top_left_ring_slot] [int] NOT NULL,
	[top_right_ring_slot] [int] NOT NULL,
	[bottom_left_ring_slot] [int] NOT NULL,
	[bottom_right_ring_slot] [int] NOT NULL,
	[boots_slot] [int] NOT NULL,
	[left_special_slot_1] [int] NOT NULL,
	[left_special_slot_2] [int] NOT NULL,
	[left_special_slot_3] [int] NOT NULL,
	[left_special_slot_4] [int] NOT NULL,
	[left_special_slot_5] [int] NOT NULL,
	[left_special_slot_6] [int] NOT NULL,
	[left_special_slot_7] [int] NOT NULL,
	[left_special_slot_8] [int] NOT NULL,
	[left_special_slot_9] [int] NOT NULL,
	[weapon_slot] [int] NOT NULL,
	[ammo_slot] [int] NOT NULL,
	[mapbook_slot] [int] NOT NULL,
	[recipebook_slot] [int] NOT NULL,
	[mantrabook_slot] [int] NOT NULL,
	[inkpot_slot] [int] NOT NULL,
	[islandtoken_slot] [int] NOT NULL,
	[speedhackmantra_slot] [int] NOT NULL,
	[money_slot] [int] NOT NULL,
	[travelbag_slot] [int] NOT NULL,
	[key_slot_1] [int] NOT NULL,
	[key_slot_2] [int] NOT NULL,
	[mission_slot] [int] NOT NULL,
	[inventory_slot_1] [int] NOT NULL,
	[inventory_slot_2] [int] NOT NULL,
	[inventory_slot_3] [int] NOT NULL,
	[inventory_slot_4] [int] NOT NULL,
	[inventory_slot_5] [int] NOT NULL,
	[inventory_slot_6] [int] NOT NULL,
	[inventory_slot_7] [int] NOT NULL,
	[inventory_slot_8] [int] NOT NULL,
	[inventory_slot_9] [int] NOT NULL,
	[inventory_slot_10] [int] NOT NULL,
	[money] [int] NOT NULL,
	[spec_level] [int] NOT NULL,
	[spec_type] [int] NOT NULL,
	[clan_id] [int] NOT NULL,
	[clan_rank] [int] NOT NULL,
 CONSTRAINT [PK_CHARACTERS] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[characters]  WITH CHECK ADD  CONSTRAINT [FK_CHARACTERS_CLANS] FOREIGN KEY([clan_id])
REFERENCES [dbo].[clans] ([id])
GO

ALTER TABLE [dbo].[characters] CHECK CONSTRAINT [FK_CHARACTERS_CLANS]
GO

