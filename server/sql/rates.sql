-- Rates del servidor. Se guardan en la DB, no en codigo.
-- Aplicar con:  server\db.cmd server\sql\rates.sql   y despues reiniciar: docker restart openmu-startup
-- La rate de experiencia final es GameConfiguration.ExperienceRate * GameServerDefinition.ExperienceRate.

-- Experiencia x5 (global)
update config."GameConfiguration" set "ExperienceRate" = 5;

-- Experiencia master x1 (sin cambios por ahora)
update config."GameConfiguration" set "MasterExperienceRate" = 1;

-- Zen x2: atributo global "Money Drop Amount Rate" (Stats.MoneyAmountRate)
update config."ConstValueAttribute" set "Value" = 2
 where "DefinitionId" = 'd84d1a5c-3a56-4cb9-8dd4-158afd4d1edb'
   and "GameConfigurationId" is not null;

-- Verificacion
select "ExperienceRate", "MasterExperienceRate" from config."GameConfiguration";
select "Value" as "ZenRate" from config."ConstValueAttribute"
 where "DefinitionId" = 'd84d1a5c-3a56-4cb9-8dd4-158afd4d1edb' and "GameConfigurationId" is not null;
