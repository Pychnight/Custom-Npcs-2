﻿using Microsoft.Xna.Framework;
using NLua;
using OTAPI.Tile;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;

namespace CustomNpcs
{
	public static class TileFunctions
	{
		public const int TileSize = 16;
		public const int HalfTileSize = TileSize / 2;

		public static ReadOnlyCollection<Point> GetOverlappedTiles(Rectangle bounds)
		{
			var min = bounds.TopLeft().ToTileCoordinates();
			var max = bounds.BottomRight().ToTileCoordinates();
			var tileCollisions = GetNonEmptyTiles(min.X, min.Y, max.X, max.Y);

			return tileCollisions;
		}

		public static ReadOnlyCollection<Point> GetNonEmptyTiles(int minColumn, int minRow, int maxColumn, int maxRow)
		{
			var results = new List<Point>();

			//clip to tileset
			minColumn = Math.Max(minColumn, 0);
			minRow = Math.Max(minRow, 0);
			maxColumn = Math.Min(maxColumn, Main.tile.Width - 1);
			maxRow = Math.Min(maxRow, Main.tile.Height - 1);

			for( var row = minRow; row <= maxRow; row++ )
			{
				for( var col = minColumn; col <= maxColumn; col++ )
				{
					var tile = Main.tile[col, row];
					var isActive = tile.active();
					//if( isActive &&
					//	( tile.type >= Tile.Type_Solid && tile.type <= Tile.Type_SlopeUpLeft || tile.wall != 0 ) ) // 0 - 5
					//{
					//	results.Add(new Point(col, row));
					//}

					if( !isActive )
						continue;

					var isEmpty = WorldGen.TileEmpty(col, row);

					if( !isEmpty || tile.wall != 0 || tile.liquid !=0 )
					{
						results.Add(new Point(col, row));

						//if(WorldGen.SolidOrSlopedTile(col,row))
						//{
						//	results.Add(new Point(col, row)); 
						//}
						//else
						//{

						//}
					}
				}
			}

			return results.AsReadOnly();
		}

		//[LuaGlobal]
		public static int TileX(float x)
		{
			return (int)( x / TileSize );
		}

		//[LuaGlobal]
		public static int TileY(float y)
		{
			return (int)( y / TileSize );
		}

		/// <summary>
		///     Gets the tile at the specified coordinates.
		/// </summary>
		/// <param name="x">The X coordinate, which must be in bounds.</param>
		/// <param name="y">The Y coordinate, which must be in bounds.</param>
		/// <returns>The tile.</returns>
		[LuaGlobal]
		public static ITile GetTile(int x, int y) => Main.tile[x, y];

		//[LuaGlobal]
		//public static bool SolidTile(ITile tile)
		//{
		//	return tile.active() && tile.type < Main.maxTileSets && Main.tileSolid[tile.type];
		//}

		[LuaGlobal]
		public static bool IsSolidTile(int column, int row)
		{
			return WorldGen.SolidTile(column, row);
		}

		[LuaGlobal]
		public static bool IsSolidOrSlopedTile(int column, int row)
		{
			return WorldGen.SolidOrSlopedTile(column, row);
		}

		[LuaGlobal]
		public static bool IsWallTile(int column, int row)
		{
			var tile = GetTile(column, row);
			return tile.wall > 0;
		}

		[LuaGlobal]
		public static bool IsLiquidTile(int column, int row)
		{
			var tile = GetTile(column, row);
			return tile.liquid > 0;
		}

		//public static bool IsWaterTile(int column, int row)
		//{
		//	var tile = GetTile(column, row);
		//	return tile.liquid > 0 && tile.liquidType() == 0;
		//}

		//public static bool IsTileLiquid(ITile tile)
		//{
		//	return tile.active() && tile.liquid 
		//}

		[LuaGlobal]
		public static void SetTile(int column, int row, int type)
		{
			if( Main.tile[column, row]?.active()==true )
			{
				Main.tile[column, row].ResetToType((ushort)type);
				TSPlayer.All.SendTileSquare(column, row);
			}
		}

		[LuaGlobal]
		public static void KillTile(int column, int row)
		{
			if(Main.tile[column,row]?.active()==true)
			{
				WorldGen.KillTile(column, row);
				TSPlayer.All.SendTileSquare(column, row);
			}
		}
		
		[LuaGlobal]
		public static void RadialKillTile(int x, int y, int radius)
		{
			var box = new Rectangle(x - radius, y - radius, radius * 2, radius * 2);
			var hits = GetOverlappedTiles(box);
			var tileCenterOffset = new Vector2(HalfTileSize, HalfTileSize);
			var center = new Vector2(x, y);

			foreach(var hit in hits)
			{
				var tileCenter = new Vector2(hit.X * TileSize,hit.Y * TileSize);
				tileCenter += tileCenterOffset;

				var dist = tileCenter - center;
								
				if( dist.LengthSquared() <= (radius * radius))
				{
					KillTile(hit.X, hit.Y);
				}
			}
		}

		//[LuaGlobal]
		//public static void RadialKillTile(Vector2 position, int radius)
		//{
		//	RadialKillTile((int)position.X, (int)position.Y, radius);
		//}

		[LuaGlobal]
		public static void RadialSetTile(int x, int y, int radius, int type)
		{
			var box = new Rectangle(x - radius, y - radius, radius * 2, radius * 2);
			var hits = GetOverlappedTiles(box);
			var tileCenterOffset = new Vector2(HalfTileSize, HalfTileSize);
			var center = new Vector2(x, y);

			foreach( var hit in hits )
			{
				var tileCenter = new Vector2(hit.X * TileSize, hit.Y * TileSize);
				tileCenter += tileCenterOffset;

				var dist = tileCenter - center;

				if( dist.LengthSquared() <= ( radius * radius ) )
				{
					SetTile(hit.X, hit.Y, type);
				}
			}
		}
	}
}