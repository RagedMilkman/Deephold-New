using FishNet.Object;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Items.Weapons
{
    public interface IShootable
    {
        /// Root that owns this hitbox (used to ignore self-hits)
        Transform OwnerRoot { get; }

        /// Can we apply a shot right now? (teams, invuln, etc.)
        bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal);

        /// Apply the effect of being shot (damage, break, etc.)
        void ServerOnShot(NetworkObject shooter, float damage, float force, Vector3 point, Vector3 normal);
    }
}
